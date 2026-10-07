using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class SummaryStoreTests : IDisposable
{
    private readonly string _dir;

    public SummaryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ScreenTranslatorSummary_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    [Fact]
    public void Save_then_load_roundtrips_summary_by_window_title()
    {
        var store = new SummaryStore(_dir);
        store.Save("我的游戏窗口", "这是长期记忆");

        Assert.Equal("这是长期记忆", store.Load("我的游戏窗口"));
        Assert.Null(store.Load("不存在的窗口"));
    }

    [Fact]
    public void Save_null_deletes_stored_summary()
    {
        var store = new SummaryStore(_dir);
        store.Save("窗口A", "摘要");
        Assert.Equal("摘要", store.Load("窗口A"));

        store.Save("窗口A", null);
        Assert.Null(store.Load("窗口A"));
    }

    [Fact]
    public void Window_title_with_invalid_chars_roundtrips()
    {
        var store = new SummaryStore(_dir);
        store.Save("窗口: A/B*C", "内容");
        Assert.Equal("内容", store.Load("窗口: A/B*C"));
    }

    [Fact]
    public void Last_process_name_roundtrips_and_can_be_cleared()
    {
        var store = new SummaryStore(_dir);
        Assert.Null(store.LoadLastProcessName());

        store.SaveLastProcessName("game.exe");
        Assert.Equal("game.exe", store.LoadLastProcessName());

        store.SaveLastProcessName(null);
        Assert.Null(store.LoadLastProcessName());
    }

    [Fact]
    public void Story_appends_and_reads_tail()
    {
        var store = new SummaryStore(_dir);
        Assert.Null(store.LoadStory("game.exe"));

        store.AppendStory("game.exe", "第一段剧情");
        store.AppendStory("game.exe", "第二段剧情");

        var full = store.LoadStory("game.exe")!;
        Assert.Contains("第一段剧情", full);
        Assert.Contains("第二段剧情", full);

        var tail = store.LoadStoryTail("game.exe", 1)!;
        Assert.Contains("第二段剧情", tail);
        Assert.DoesNotContain("第一段剧情", tail);
    }
}
