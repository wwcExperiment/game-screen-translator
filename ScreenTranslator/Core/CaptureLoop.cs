namespace ScreenTranslator.Core;

/// <summary>
/// 驱动「监视 → 翻译」循环。由定时器周期性调用 <see cref="Tick"/>：
/// 截图 → 检测变化 → 等画面稳定（去抖）→ 发一次给模型。
/// 画面持续变化时，超过当前超时（初始 <see cref="_maxWait"/>）且无在途请求，则强制翻译一次；
/// 若两次译文几乎相同（动画等无意义变化），超时成倍退避，避免反复翻译相同内容。
/// 同一时刻最多一个在途请求；普通小变化只标记待重译，仅显著变化才取消在途请求。
/// </summary>
public sealed class CaptureLoop
{
    private readonly IFrameSource _source;
    private readonly IChangeDetector _detector;
    private readonly ITranslator _translator;
    private readonly IClock _clock;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maxWait;
    private readonly TimeSpan _maxWaitCeiling;
    private readonly double _similarThreshold;

    private DateTime? _lastChangeUtc;
    private DateTime? _changeAnchorUtc;
    private bool _sentForCurrentContent;
    private CancellationTokenSource? _inFlight;
    private byte[]? _lastPng;

    private TimeSpan _currentMaxWait;
    private string? _lastTranslation;

    /// <param name="debounceMs">内容变化后需要保持稳定的时长（毫秒），超过才发送。</param>
    /// <param name="maxWaitMs">画面持续变化时的初始超时（毫秒）：超过该时长且无在途请求时强制翻译一次。</param>
    /// <param name="maxWaitCeilingMs">超时退避上限（毫秒）：连续多次译文几乎相同时，超时最多放大到该值。</param>
    /// <param name="similarThreshold">两次译文「几乎相同」的相似度阈值（0~1，越大越严格）。</param>
    public CaptureLoop(IFrameSource source, IChangeDetector detector, ITranslator translator, IClock clock,
        int debounceMs, int maxWaitMs = 1000, int maxWaitCeilingMs = 30000, double similarThreshold = 0.8)
    {
        _source = source;
        _detector = detector;
        _translator = translator;
        _clock = clock;
        _debounce = TimeSpan.FromMilliseconds(debounceMs);
        _maxWait = TimeSpan.FromMilliseconds(maxWaitMs);
        _maxWaitCeiling = TimeSpan.FromMilliseconds(maxWaitCeilingMs);
        _similarThreshold = similarThreshold;
        _currentMaxWait = _maxWait;
    }

    /// <summary>翻译完成时触发（可能在后台线程）。</summary>
    public event Action<string>? Translated;

    /// <summary>翻译失败时触发（可能在后台线程）。</summary>
    public event Action<Exception>? Failed;

    /// <summary>开始发送翻译请求时触发（在 <see cref="Tick"/> 调用线程，通常为 UI 线程）。</summary>
    public event Action? Translating;

    public void Tick()
    {
        var first = _lastPng is null; // 启动或重选区域后的首帧
        var frame = _source.Capture();
        _lastPng = frame.PngBytes;
        var changed = _detector.IsChanged(frame.Signature, out var significant);

        if (first)
        {
            // 首帧是「初始画面」而非「内容变化」：直接送去翻译，无需等待去抖。
            Log.Write($"首帧：直接发送截图 ({frame.PngBytes.Length} 字节)");
            Send(frame.PngBytes);
            return;
        }

        var now = _clock.UtcNow;

        if (changed)
        {
            // 出现变化：若当前内容已发送过，则开启新一轮（记录本轮起始用于最大等待上限）；
            // 无论是否已发送，都刷新「最近变化时间」用于去抖。
            if (_sentForCurrentContent)
            {
                _changeAnchorUtc = now;
                _sentForCurrentContent = false;
            }
            _lastChangeUtc = now;

            // 仅「显著变化」才取消在途请求；普通小变化只标记待重译，不打断在途请求。
            if (significant)
            {
                Log.Write("显著变化：取消在途请求");
                _inFlight?.Cancel();
                _inFlight = null;
            }
        }

        if (_sentForCurrentContent || _lastChangeUtc is null)
            return;

        var stableFor = now - _lastChangeUtc.Value;
        var sinceAnchor = _changeAnchorUtc is null ? stableFor : now - _changeAnchorUtc.Value;

        // 画面稳定超过去抖时长，或本轮变化持续过久（达到当前超时上限）时，才允许发送。
        if (stableFor < _debounce && sinceAnchor < _currentMaxWait)
            return;

        // 有请求在途（未被显著变化取消）：等它完成后，由下一个 Tick 补发最新一帧。
        if (_inFlight is not null)
            return;

        Log.Write($"发送截图（稳定 {stableFor.TotalMilliseconds:0}ms，距本轮首变 {sinceAnchor.TotalMilliseconds:0}ms，当前超时 {_currentMaxWait.TotalMilliseconds:0}ms）");
        Send(frame.PngBytes);
    }

    private void Send(byte[] pngBytes)
    {
        _sentForCurrentContent = true;
        _changeAnchorUtc = null;
        var cts = new CancellationTokenSource();
        _inFlight = cts;
        Translating?.Invoke();
        _ = TranslateAndPublishAsync(pngBytes, cts);
    }

    /// <summary>重新翻译最后一次截图（例如编辑提示词后点击「继续」时）。</summary>
    public void RetranslateLast()
    {
        var png = _lastPng;
        if (png is null)
            return;
        Send(png);
    }

    private async Task TranslateAndPublishAsync(byte[] pngBytes, CancellationTokenSource cts)
    {
        var started = DateTime.UtcNow;
        try
        {
            var text = await _translator.TranslateAsync(pngBytes, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Log.Write($"翻译完成 ({(DateTime.UtcNow - started).TotalMilliseconds:0}ms): {TruncateForLog(text)}");
                AdjustBackoff(text);
                Translated?.Invoke(text);
            }
        }
        catch (OperationCanceledException)
        {
            // 请求被「显著变化」取消（或外部超时），无需发布结果。
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                Log.Write($"翻译失败: {ex.GetType().Name}: {ex.Message}");
                Failed?.Invoke(ex);
            }
        }
        finally
        {
            // 请求结束：若它仍是在途那条，则清空，让后续 Tick 能发送最新内容。
            if (ReferenceEquals(_inFlight, cts))
                _inFlight = null;
            // 故意不 Dispose cts：Dispose 后再 Cancel 会抛 ObjectDisposedException，交给 GC 回收即可（桌面程序，量极小）。
        }
    }

    /// <summary>翻译完成后根据内容相似度调整超时：几乎相同则退避，有实质变化则恢复初始超时。</summary>
    private void AdjustBackoff(string text)
    {
        var translation = TranslationSplitter.Split(text).Translation;

        if (_lastTranslation is not null &&
            TextSimilarity.AreNearlySame(_lastTranslation, translation, _similarThreshold))
        {
            var doubled = TimeSpan.FromMilliseconds(_currentMaxWait.TotalMilliseconds * 2);
            _currentMaxWait = doubled > _maxWaitCeiling ? _maxWaitCeiling : doubled;
            Log.Write($"译文与上次几乎相同，超时退避至 {_currentMaxWait.TotalMilliseconds:0}ms");
        }
        else
        {
            _currentMaxWait = _maxWait;
        }
        _lastTranslation = translation;
    }

    private static string TruncateForLog(string text)
    {
        var flat = string.IsNullOrWhiteSpace(text) ? "<空>" : text.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 100 ? flat : flat[..100] + "…";
    }
}
