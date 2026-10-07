using System.Text;
using System.Threading;

namespace ScreenTranslator.Core;

/// <summary>
/// 维护对话历史：每次翻译请求滚动携带最近若干轮历史（文本），帮助模型理解上下文；
/// 当历史超过上限时，让模型自行压缩成一段长期摘要，并只保留最近几轮。
/// 另有独立于近期摘要的「剧情梗概」：按进程名累积追加到磁盘，不随对话给模型。
/// </summary>
public sealed class ConversationManager : ITranslator
{
    /// <summary>剧情补充时回看的已有梗概末尾行数。</summary>
    private const int StoryTailLines = 20;

    /// <summary>近期摘要的目标长度上限（字符）。</summary>
    private const int SummaryCharLimit = 400;

    /// <summary>近期摘要的兜底长度上限（两倍目标），超出部分用字符串硬裁剪。</summary>
    private const int SummaryCharHardLimit = SummaryCharLimit * 2;

    private readonly ITranslationClient _client;
    private readonly IStoryStore? _storyStore;
    private string _prompt;
    private string _summaryPrompt = AppConfig.DefaultSummaryPrompt;
    private string _storyPrompt = AppConfig.DefaultStoryPrompt;
    private string _summaryLengthPrompt = AppConfig.DefaultSummaryLengthPrompt;
    private string _summaryPrefix = AppConfig.DefaultSummaryPrefix;
    private string _userTurnMarker = AppConfig.DefaultUserTurnMarker;
    private readonly int _maxTurns;
    private readonly int _keepTurns;
    private readonly TimeSpan _idleSummarizeDelay;
    private readonly System.Threading.Timer _idleTimer;

    private readonly object _gate = new();
    private string? _summary;
    private string? _memoryKey;
    private bool _summaryEnabled = true;
    private bool _storyEnabled = true;
    private bool _compressing;
    private int _compressionCount;
    private readonly List<ChatMessage> _recent = new();
    private readonly List<ChatMessage> _storyPending = new();

    public ConversationManager(ITranslationClient client, string prompt, int maxTurns = 15, int keepTurns = 5, IStoryStore? storyStore = null, TimeSpan? idleSummarizeDelay = null)
    {
        _client = client;
        _prompt = prompt;
        _maxTurns = maxTurns;
        _keepTurns = keepTurns;
        _storyStore = storyStore;
        _idleSummarizeDelay = idleSummarizeDelay ?? TimeSpan.FromMinutes(10);
        _idleTimer = new System.Threading.Timer(_ => OnIdleTimeout(), null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>剧情梗概的存档键（目标窗口进程名）；为 null 时不存剧情。</summary>
    public string? MemoryKey
    {
        get { lock (_gate) return _memoryKey; }
        set { lock (_gate) _memoryKey = value; }
    }

    /// <summary>是否启用短期记忆（摘要压缩）。关闭后不生成、也不携带摘要。</summary>
    public bool SummaryEnabled
    {
        get { lock (_gate) return _summaryEnabled; }
        set { lock (_gate) _summaryEnabled = value; }
    }

    /// <summary>是否启用长期留档（剧情梗概写盘）。</summary>
    public bool StoryEnabled
    {
        get { lock (_gate) return _storyEnabled; }
        set { lock (_gate) _storyEnabled = value; }
    }

    /// <summary>长期摘要更新后触发（可能在后台线程）。</summary>
    public event Action? SummaryChanged;

    public string? Summary { get { lock (_gate) return _summary; } }

    /// <summary>设置长期摘要（例如启动/切换窗口时从磁盘恢复长期记忆）。</summary>
    public void SetSummary(string? summary)
    {
        lock (_gate)
        {
            _summary = string.IsNullOrWhiteSpace(summary) ? null : summary;
        }
        SummaryChanged?.Invoke();
    }

    public string Prompt
    {
        get { lock (_gate) return _prompt; }
        set { lock (_gate) _prompt = value ?? ""; }
    }

    /// <summary>近期摘要压缩提示词。</summary>
    public string SummaryPrompt
    {
        get { lock (_gate) return _summaryPrompt; }
        set { lock (_gate) _summaryPrompt = string.IsNullOrWhiteSpace(value) ? AppConfig.DefaultSummaryPrompt : value; }
    }

    /// <summary>剧情梗概补充提示词。</summary>
    public string StoryPrompt
    {
        get { lock (_gate) return _storyPrompt; }
        set { lock (_gate) _storyPrompt = string.IsNullOrWhiteSpace(value) ? AppConfig.DefaultStoryPrompt : value; }
    }

    /// <summary>摘要超长补救提示词（占位符 {len}/{limit}/{summary}）。</summary>
    public string SummaryLengthPrompt
    {
        get { lock (_gate) return _summaryLengthPrompt; }
        set { lock (_gate) _summaryLengthPrompt = string.IsNullOrWhiteSpace(value) ? AppConfig.DefaultSummaryLengthPrompt : value; }
    }

    /// <summary>近期摘要作为 system 消息时的前缀。</summary>
    public string SummaryPrefix
    {
        get { lock (_gate) return _summaryPrefix; }
        set { lock (_gate) _summaryPrefix = string.IsNullOrWhiteSpace(value) ? AppConfig.DefaultSummaryPrefix : value; }
    }

    /// <summary>历史里标记「上一次截图」的占位文本。</summary>
    public string UserTurnMarker
    {
        get { lock (_gate) return _userTurnMarker; }
        set { lock (_gate) _userTurnMarker = string.IsNullOrWhiteSpace(value) ? AppConfig.DefaultUserTurnMarker : value; }
    }

    public async Task<string> TranslateAsync(byte[] pngBytes, CancellationToken cancellationToken)
    {
        var messages = BuildRequest(pngBytes);
        var text = await _client.TranslateAsync(messages, cancellationToken);
        if (!cancellationToken.IsCancellationRequested)
            RecordExchange(text);
        return text;
    }

    public IReadOnlyList<ChatMessage> BuildRequest(byte[] png)
    {
        lock (_gate)
        {
            var messages = new List<ChatMessage>(_recent.Count + 2);
            if (_summaryEnabled && _summary is not null)
                messages.Add(new ChatMessage { Role = "system", Text = _summaryPrefix + _summary });
            messages.AddRange(_recent);
            messages.Add(new ChatMessage { Role = "user", Text = _prompt, ImagePng = png });
            return messages;
        }
    }

    /// <summary>记录一轮对话；若历史超限则在后台异步压缩（不阻塞翻译返回）。</summary>
    public void RecordExchange(string translation)
    {
        // 无文字的译文（模型在图片无文字时可能返回「无文字」「无可翻译内容」等占位说明）
        // 不作为上下文记录，避免污染近期摘要与长期归档的输入。原文或译文任一命中占位都算无文字。
        if (TranslationSplitter.IsNoText(TranslationSplitter.Split(translation)))
            return;

        var userMsg = new ChatMessage { Role = "user", Text = _userTurnMarker };
        var assistantMsg = new ChatMessage { Role = "assistant", Text = translation };

        bool overflow;
        lock (_gate)
        {
            _recent.Add(userMsg);
            _recent.Add(assistantMsg);

            // 剧情待归档列表独立累积，不随滚动窗口裁剪而丢失；设上限防止长期关闭剧情归档时无限增长
            _storyPending.Add(userMsg);
            _storyPending.Add(assistantMsg);
            var storyPendingMax = _maxTurns * 4;
            if (_storyPending.Count > storyPendingMax)
                _storyPending.RemoveRange(0, _storyPending.Count - storyPendingMax);

            overflow = _recent.Count > _maxTurns * 2;
        }

        ResetIdleTimer(); // 有新翻译，重置「静置超时总结」计时

        if (overflow)
            StartCompression();
    }

    /// <summary>取快照并在后台启动一次压缩；静置超时与历史超限共用。</summary>
    private void StartCompression()
    {
        List<ChatMessage>? snapshot = null;
        lock (_gate)
        {
            if (_recent.Count > 0 && !_compressing)
            {
                _compressing = true;
                snapshot = new List<ChatMessage>(_recent); // 压缩前的完整快照

                // 立即把滚动窗口裁剪到最近 keepTurns 轮（快照已含全部历史）
                var keep = _keepTurns * 2;
                if (_recent.Count > keep)
                    _recent.RemoveRange(0, _recent.Count - keep);
            }
        }

        if (snapshot is not null)
            _ = CompressAsync(snapshot); // 后台执行，不阻塞译文返回
    }

    private void ResetIdleTimer()
    {
        _idleTimer.Change(_idleSummarizeDelay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private void OnIdleTimeout()
    {
        StartCompression();
    }

    private async Task CompressAsync(List<ChatMessage> snapshot)
    {
        try
        {
            bool summaryEnabled, storyEnabled;
            lock (_gate)
            {
                summaryEnabled = _summaryEnabled;
                storyEnabled = _storyEnabled;
            }

            // 只归档「即将被裁剪掉」的旧内容；最近 keepTurns 轮仍保留在滚动窗口里、每次随请求发送，
            // 无需重复写进摘要。空闲总结时若不足 keepTurns 轮则无可归档内容，跳过短期记忆总结。
            var keep = _keepTurns * 2;
            var toArchive = snapshot.Take(Math.Max(0, snapshot.Count - keep)).ToList();

            if (summaryEnabled && toArchive.Count > 0)
            {
                string? oldSummary;
                lock (_gate)
                {
                    oldSummary = _summary;
                }

                var messages = new List<ChatMessage>(toArchive.Count + 2);
                if (oldSummary is not null)
                    messages.Add(new ChatMessage { Role = "system", Text = _summaryPrefix + oldSummary });
                messages.AddRange(toArchive);
                messages.Add(new ChatMessage
                {
                    Role = "user",
                    Text = _summaryPrompt,
                });

                string summary;
                try
                {
                    summary = await _client.SummarizeAsync(messages, CancellationToken.None);
                }
                catch
                {
                    summary = ""; // 短期记忆总结失败不阻断剧情
                }

                // 二次检测：模型经常超长，先用加重语气要求裁剪；仍超两倍上限则字符串硬裁。
                if (!string.IsNullOrWhiteSpace(summary) && summary.Length > SummaryCharLimit)
                    summary = await EnforceSummaryLength(summary);

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    lock (_gate)
                    {
                        _summary = summary;
                    }
                    SummaryChanged?.Invoke();
                }
            }

            // 剧情梗概：增量补充（独立于近期摘要，不随对话给模型）；每两次压缩执行一次
            bool doStory;
            lock (_gate)
            {
                _compressionCount++;
                doStory = _compressionCount % 2 == 0;
            }
            if (storyEnabled && doStory)
                await AppendStoryAsync();
        }
        finally
        {
            lock (_gate)
            {
                _compressing = false;
            }
        }
    }

    /// <summary>
    /// 近期摘要超长时的补救：先用加重语气要求模型裁剪到目标上限；
    /// 若模型仍不配合、长度超过两倍上限，则直接用字符串硬裁到两倍上限内，保证摘要不会无限膨胀。
    /// </summary>
    private async Task<string> EnforceSummaryLength(string summary)
    {
        string trimmed;
        try
        {
            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "user",
                    Text = _summaryLengthPrompt.Replace("{len}", summary.Length.ToString()).Replace("{limit}", SummaryCharLimit.ToString()).Replace("{summary}", summary),
                },
            };
            trimmed = await _client.SummarizeAsync(messages, CancellationToken.None);
        }
        catch
        {
            trimmed = "";
        }

        if (!string.IsNullOrWhiteSpace(trimmed))
            summary = trimmed;

        // 兜底：模型仍不配合时，用字符串裁剪保证不超过两倍上限。
        if (summary.Length > SummaryCharHardLimit)
            summary = summary[..SummaryCharHardLimit];

        return summary;
    }

    private async Task AppendStoryAsync()
    {
        if (_storyStore is null)
            return;
        string? key;
        List<ChatMessage> pending;
        lock (_gate)
        {
            key = _memoryKey;
            // 取出自上次归档以来的全部对话并清空待归档列表（无论是否归档都清空，避免累积）
            pending = new List<ChatMessage>(_storyPending);
            _storyPending.Clear();
        }

        if (string.IsNullOrWhiteSpace(key))
            return; // 没选窗口不存剧情（待归档已清空）

        // 只取 assistant 消息并提取译文部分（原文不入剧情），跳过 [此前截图] 占位与空译文
        var recentLines = pending
            .Where(m => m.Role == "assistant" && !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => TranslationSplitter.Split(m.Text).Translation.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (recentLines.Count == 0)
            return; // 没有有效译文，无可总结

        var tail = _storyStore.LoadStoryTail(key, StoryTailLines);

        var prompt = new StringBuilder(_storyPrompt);
        if (!string.IsNullOrWhiteSpace(tail))
        {
            prompt.AppendLine();
            prompt.AppendLine("【已归档的剧情梗概（最后部分）】");
            prompt.Append(tail);
        }
        prompt.AppendLine();
        prompt.AppendLine("【新增对话】");
        foreach (var text in recentLines)
            prompt.AppendLine($"- {text}");

        LogStoryDebug("输入", prompt.ToString());

        string addition;
        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Text = prompt.ToString() },
            };
            addition = await _client.SummarizeAsync(messages, CancellationToken.None);
        }
        catch
        {
            return;
        }

        LogStoryDebug("输出", addition ?? "<null>");

        if (string.IsNullOrWhiteSpace(addition))
            return;
        _storyStore.AppendStory(key, addition);
    }

    /// <summary>把长期归档的输入/输出写到调试日志（logs/story_debug.log），便于排查；失败不影响主流程。</summary>
    private static void LogStoryDebug(string tag, string content)
    {
        try
        {
            const string dir = "logs";
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "story_debug.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {tag} ===\n{content}\n\n",
                Encoding.UTF8);
        }
        catch
        {
            // 调试日志失败不影响主流程
        }
    }
}
