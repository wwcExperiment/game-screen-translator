using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class CaptureLoopTests
{
    // 4×4 签名、块大小 1：共 16 块，每块即 1 像素，便于精确控制「小变化 / 显著变化」。
    private static readonly byte[] SigEmpty = new byte[16];
    private static readonly byte[] SigSmall = MakeSig(4);   // 4/16=0.25：有变化，但未达取消阈值 0.5
    private static readonly byte[] SigBig = MakeSig(12);    // 12/16=0.75：显著变化，达到取消阈值 0.5
    private static readonly byte[] PngA = { 1, 2, 3 };
    private static readonly byte[] PngB = { 4, 5, 6 };

    private static byte[] MakeSig(int changedPixels)
    {
        var s = new byte[16];
        for (var i = 0; i < changedPixels && i < s.Length; i++) s[i] = 200;
        return s;
    }

    private sealed class DelegateSource : IFrameSource
    {
        public Func<Frame> CaptureImpl { get; set; } = () => throw new InvalidOperationException("未配置帧");
        public Frame Capture() => CaptureImpl();
    }

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(int ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private sealed class FakeTranslator : ITranslator
    {
        public string Prompt { get; set; } = "";
        public string? Summary { get; set; }
        public void SetSummary(string? summary) => Summary = summary;
        public string? MemoryKey { get; set; }
        public bool SummaryEnabled { get; set; } = true;
        public bool StoryEnabled { get; set; } = true;
#pragma warning disable CS0067 // 事件仅用于满足接口，测试未触发
        public event Action? SummaryChanged;
#pragma warning restore CS0067
        public readonly List<byte[]> Requests = new();
        public readonly List<CancellationToken> Tokens = new();
        public Func<byte[], CancellationToken, Task<string>>? Handler;

        public Task<string> TranslateAsync(byte[] png, CancellationToken ct)
        {
            Requests.Add(png);
            Tokens.Add(ct);
            return Handler?.Invoke(png, ct) ?? Task.FromResult("译文");
        }
    }

    private static (CaptureLoop loop, FakeTranslator client, FakeClock clock, DelegateSource source) Make(int debounceMs = 500, int maxWaitMs = 1000, int maxWaitCeilingMs = 30000)
    {
        var client = new FakeTranslator();
        var clock = new FakeClock();
        var source = new DelegateSource();
        var loop = new CaptureLoop(source, new ChangeDetector(4, 4, 0.1, 0.5, 10, 1), client, clock, debounceMs, maxWaitMs, maxWaitCeilingMs);
        return (loop, client, clock, source);
    }

    [Fact]
    public void First_frame_sends_immediately()
    {
        var (loop, client, clock, source) = Make();
        source.CaptureImpl = () => new Frame(SigEmpty, PngA);

        loop.Tick(); // 首帧 → 直接翻译初始画面

        Assert.Single(client.Requests);
        Assert.Equal(PngA, client.Requests[0]);
    }

    [Fact]
    public void Stable_content_after_debounce_sends_exactly_once()
    {
        var (loop, client, clock, source) = Make();
        source.CaptureImpl = () => new Frame(SigEmpty, PngA);

        loop.Tick(); // 变化
        clock.Advance(600);
        loop.Tick(); // 稳定且超过去抖 → 发送
        loop.Tick(); // 仍稳定 → 不再发

        Assert.Single(client.Requests);
        Assert.Equal(PngA, client.Requests[0]);
    }

    [Fact]
    public void New_change_after_send_triggers_a_new_send()
    {
        var (loop, client, clock, source) = Make();
        source.CaptureImpl = () => new Frame(SigEmpty, PngA);

        loop.Tick();
        clock.Advance(600);
        loop.Tick(); // 发 A
        Assert.Single(client.Requests);

        source.CaptureImpl = () => new Frame(SigBig, PngB);
        loop.Tick(); // 变为 B → 重置
        clock.Advance(600);
        loop.Tick(); // 稳定 B → 发 B

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(PngB, client.Requests[1]);
    }

    [Fact]
    public void Significant_change_cancels_inflight_request()
    {
        var (loop, client, clock, source) = Make();
        client.Handler = (_, ct) =>
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        };

        source.CaptureImpl = () => new Frame(SigEmpty, PngA);
        loop.Tick(); // 首帧 → 发 A（在途）
        clock.Advance(600);
        loop.Tick(); // 稳定，已发过 → 不取消

        Assert.Single(client.Requests);
        Assert.False(client.Tokens[0].IsCancellationRequested);

        source.CaptureImpl = () => new Frame(SigBig, PngB);
        loop.Tick(); // 显著变化 → 取消在途请求

        Assert.True(client.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public void Small_change_does_not_cancel_inflight_request()
    {
        var (loop, client, clock, source) = Make();
        client.Handler = (_, ct) =>
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        };

        source.CaptureImpl = () => new Frame(SigEmpty, PngA);
        loop.Tick(); // 首帧 → 发 A（在途）
        clock.Advance(600);
        loop.Tick(); // 稳定，已发过 → 不取消

        Assert.Single(client.Requests);
        Assert.False(client.Tokens[0].IsCancellationRequested);

        source.CaptureImpl = () => new Frame(SigSmall, PngB);
        loop.Tick(); // 小变化 → 只标记未发送，不取消在途请求
        clock.Advance(600);
        loop.Tick(); // 稳定且超过去抖，但在途请求未结束 → 仍不发送、不取消

        Assert.Single(client.Requests);
        Assert.False(client.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public void Continuous_change_forces_send_after_max_wait()
    {
        var (loop, client, clock, source) = Make(debounceMs: 500); // maxWait 默认 1000
        source.CaptureImpl = () => new Frame(SigEmpty, PngA);

        loop.Tick(); // 首帧 → 发 A
        Assert.Single(client.Requests);

        // 之后每一帧都在小变化（SigEmpty/SigSmall 交替），去抖永远不满足；超过 maxWait 上限后仍要强制发送
        var toggle = false;
        source.CaptureImpl = () => new Frame(toggle ? SigSmall : SigEmpty, PngB);
        for (var i = 0; i < 30; i++)
        {
            toggle = !toggle;
            clock.Advance(100);
            loop.Tick();
        }

        Assert.True(client.Requests.Count >= 2, "连续变化超过 maxWait 后应强制补发一次");
    }

    [Fact]
    public async Task Translated_event_raises_with_model_output()
    {
        var (loop, client, clock, source) = Make();
        client.Handler = (_, _) => Task.FromResult("你好");

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Translated += tcs.SetResult;

        source.CaptureImpl = () => new Frame(SigEmpty, PngA);
        loop.Tick();
        clock.Advance(600);
        loop.Tick(); // 发送

        var text = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("你好", text);
    }

    [Fact]
    public async Task Failed_event_raises_on_exception()
    {
        var (loop, client, clock, source) = Make();
        client.Handler = (_, _) => Task.FromException<string>(new InvalidOperationException("boom"));

        var tcs = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        loop.Failed += tcs.SetResult;

        source.CaptureImpl = () => new Frame(SigEmpty, PngA);
        loop.Tick();
        clock.Advance(600);
        loop.Tick(); // 发送并失败

        var ex = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void RetranslateLast_resends_last_png_with_current_prompt()
    {
        var (loop, client, clock, source) = Make();
        source.CaptureImpl = () => new Frame(SigEmpty, PngA);

        loop.Tick(); // 首帧变化
        clock.Advance(600);
        loop.Tick(); // 稳定后发 A
        Assert.Single(client.Requests);

        client.Prompt = "新提示词";
        string? promptAtRequest = null;
        client.Handler = (_, _) =>
        {
            promptAtRequest = client.Prompt;
            return Task.FromResult("译文");
        };

        loop.RetranslateLast(); // 用新提示词重发最后一张截图

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(PngA, client.Requests[1]);
        Assert.Equal("新提示词", promptAtRequest);
    }

    [Fact]
    public void Similar_then_different_translation_backs_off_and_resets()
    {
        var (loop, client, clock, source) = Make(debounceMs: 500, maxWaitMs: 1000, maxWaitCeilingMs: 100000);
        var sendTicks = new List<long>();
        var call = 0;
        client.Handler = (_, _) =>
        {
            call++;
            sendTicks.Add(clock.UtcNow.Ticks);
            return Task.FromResult(call == 3 ? "完全不同" : "相同内容");
        };

        source.CaptureImpl = () => new Frame(SigEmpty, PngA);
        loop.Tick(); // 首帧

        // 之后画面持续小变化，靠超时强制发送；译文前两次相同、第三次不同。
        var toggle = false;
        source.CaptureImpl = () => new Frame(toggle ? SigSmall : SigEmpty, PngB);
        for (var i = 0; i < 50; i++)
        {
            toggle = !toggle;
            clock.Advance(100);
            loop.Tick();
        }

        Assert.True(sendTicks.Count >= 4, "应至少发送 4 次");
        var gap1 = sendTicks[1] - sendTicks[0]; // 相同译文 → 退避前
        var gap2 = sendTicks[2] - sendTicks[1]; // 相同译文 → 退避后
        var gap3 = sendTicks[3] - sendTicks[2]; // 不同译文 → 重置后
        Assert.True(gap2 > gap1, "译文与上次相同时，超时应退避（间隔增大）");
        Assert.True(gap3 < gap2, "译文与上次不同时，超时应恢复（间隔缩小）");
    }
}
