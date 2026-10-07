using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class ConversationManagerTests
{
    private sealed class RecordingClient : ITranslationClient
    {
        public readonly List<IReadOnlyList<ChatMessage>> Calls = new();
        public Func<IReadOnlyList<ChatMessage>, Task<string>> Handler = _ => Task.FromResult("译文");

        public Task<string> TranslateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            Calls.Add(messages);
            return Handler(messages);
        }

        public Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            Calls.Add(messages);
            return Handler(messages);
        }
    }

    private static byte[] Png(int seed) => new byte[] { (byte)seed };

    [Fact]
    public async Task TranslateAsync_carries_history_into_subsequent_requests()
    {
        var client = new RecordingClient();
        var count = 0;
        client.Handler = _ => Task.FromResult($"译{++count}");
        var manager = new ConversationManager(client, "翻译", maxTurns: 100, keepTurns: 100);

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);

        var request = manager.BuildRequest(Png(3));
        var assistantTexts = request.Where(m => m.Role == "assistant").Select(m => m.Text).ToList();
        Assert.Equal(new[] { "译1", "译2" }, assistantTexts);

        Assert.Equal(Png(3), request[^1].ImagePng);
        Assert.Equal("翻译", request[^1].Text);
    }

    [Fact]
    public async Task No_text_placeholder_in_original_is_not_recorded()
    {
        var client = new RecordingClient();
        client.Handler = _ => Task.FromResult("原文：无文字\n译文：你好");
        var manager = new ConversationManager(client, "翻译", maxTurns: 100, keepTurns: 100);

        await manager.TranslateAsync(Png(1), CancellationToken.None);

        var request = manager.BuildRequest(Png(2));
        Assert.DoesNotContain(request, m => m.Role == "assistant");
    }

    [Fact]
    public async Task Parenthesized_no_text_placeholder_is_not_recorded()
    {
        var client = new RecordingClient();
        client.Handler = _ => Task.FromResult("（无文字）");
        var manager = new ConversationManager(client, "翻译", maxTurns: 100, keepTurns: 100);

        await manager.TranslateAsync(Png(1), CancellationToken.None);

        var request = manager.BuildRequest(Png(2));
        Assert.DoesNotContain(request, m => m.Role == "assistant");
    }

    [Fact]
    public async Task Compresses_and_keeps_recent_turns_when_history_exceeds_limit()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult("这是摘要");
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1);

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 超过 2 轮 → 压缩

        Assert.Equal("这是摘要", manager.Summary);

        var request = manager.BuildRequest(Png(4));
        var assistantTexts = request.Where(m => m.Role == "assistant").Select(m => m.Text).ToList();
        Assert.Single(assistantTexts);
        Assert.Equal("译文3", assistantTexts[0]);

        Assert.Equal("system", request[0].Role);
        Assert.Contains("这是摘要", request[0].Text);
    }

    [Fact]
    public async Task Compression_archives_only_trimmed_older_turns_not_kept_recent_ones()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult("这是摘要");
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1);

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 触发压缩

        // 压缩调用：最后一条是无图片的 user 指令
        var compressCall = client.Calls.Single(c => c[^1].ImagePng is null);
        var archivedTexts = compressCall.Where(m => m.Role == "assistant").Select(m => m.Text).ToList();
        // keepTurns=1 → 保留最后 2 条（译文3），只归档更早的译文1、译文2
        Assert.Equal(new[] { "译文1", "译文2" }, archivedTexts);
    }

    [Fact]
    public void Prompt_can_be_updated_and_is_used_in_requests()
    {
        var client = new RecordingClient();
        var manager = new ConversationManager(client, "翻译");
        manager.Prompt = "Sakura 保持原文，其余翻译成中文";
        var request = manager.BuildRequest(Png(1));
        Assert.Equal("Sakura 保持原文，其余翻译成中文", request[^1].Text);
    }

    [Fact]
    public void TargetLanguage_replaces_lang_placeholder_in_prompt()
    {
        var client = new RecordingClient();
        var manager = new ConversationManager(client, "把文字翻译成{lang}") { TargetLanguage = "英语" };
        var request = manager.BuildRequest(Png(1));
        Assert.Equal("把文字翻译成英语", request[^1].Text);
    }

    [Fact]
    public void TargetLanguage_defaults_to_chinese()
    {
        var client = new RecordingClient();
        var manager = new ConversationManager(client, "翻译成{lang}");
        var request = manager.BuildRequest(Png(1));
        Assert.Equal("翻译成中文", request[^1].Text);
    }

    [Fact]
    public async Task SummaryChanged_event_raises_after_compression()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult("这是摘要");
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1);
        string? raisedSummary = null;
        manager.SummaryChanged += () => raisedSummary = manager.Summary;

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 超过 2 轮 → 压缩

        Assert.Equal("这是摘要", raisedSummary);
    }

    [Fact]
    public async Task Oversized_summary_is_retried_with_stronger_trim_instruction()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            if (last.Text!.Contains("太长了"))
                return Task.FromResult("裁剪后的摘要");
            return Task.FromResult(new string('长', 1000));
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1);

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 触发压缩，摘要超长

        Assert.Equal("裁剪后的摘要", manager.Summary);
    }

    [Fact]
    public async Task Oversized_summary_is_hard_capped_at_twice_the_limit()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult(new string('长', 1000));
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1);

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 触发压缩，摘要始终超长

        var summary = manager.Summary;
        Assert.NotNull(summary);
        Assert.Equal(800, summary!.Length);
    }

    [Fact]
    public void SetSummary_replaces_summary_and_raises_event()
    {
        var client = new RecordingClient();
        var manager = new ConversationManager(client, "翻译");
        var raised = 0;
        manager.SummaryChanged += () => raised++;

        manager.SetSummary("恢复的记忆");

        Assert.Equal("恢复的记忆", manager.Summary);
        Assert.Equal(1, raised);

        var request = manager.BuildRequest(Png(1));
        Assert.Equal("system", request[0].Role);
        Assert.Contains("恢复的记忆", request[0].Text);
    }

    [Fact]
    public async Task AppendStory_writes_plot_synopsis_when_memory_key_set()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenTranslatorStory_" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new RecordingClient();
            var store = new SummaryStore(dir);
            var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1, storyStore: store);
            manager.MemoryKey = "game.exe";

            var translationCount = 0;
            client.Handler = messages =>
            {
                var last = messages[^1];
                if (last.ImagePng is not null)
                    return Task.FromResult($"译文{++translationCount}");
                if (last.Text!.Contains("剧情梗概"))
                    return Task.FromResult("剧情补充：主角进入新区域");
                return Task.FromResult("这是摘要");
            };

            // 剧情每两次压缩执行一次：第 3 句触发第 1 次压缩（无剧情），第 5 句触发第 2 次压缩（有剧情）
            await manager.TranslateAsync(Png(1), CancellationToken.None);
            await manager.TranslateAsync(Png(2), CancellationToken.None);
            await manager.TranslateAsync(Png(3), CancellationToken.None); // 压缩1 → 无剧情
            await manager.TranslateAsync(Png(4), CancellationToken.None);
            await manager.TranslateAsync(Png(5), CancellationToken.None); // 压缩2 → 剧情

            Assert.Equal("这是摘要", manager.Summary);
            Assert.Contains("剧情补充：主角进入新区域", store.LoadStory("game.exe"));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Idle_timeout_triggers_summary_once_without_new_translation()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult("空闲总结");
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 100, keepTurns: 1,
            idleSummarizeDelay: TimeSpan.FromMilliseconds(50));

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);

        Assert.Null(manager.Summary); // 尚未静置超时

        await Task.Delay(300); // 等待静置超时触发

        Assert.Equal("空闲总结", manager.Summary);
    }

    [Fact]
    public async Task AppendStory_skips_when_no_memory_key()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenTranslatorStory_" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new RecordingClient();
            var store = new SummaryStore(dir);
            var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1, storyStore: store);
            // 未设置 MemoryKey → 不写剧情

            var translationCount = 0;
            client.Handler = messages =>
            {
                var last = messages[^1];
                if (last.ImagePng is not null)
                    return Task.FromResult($"译文{++translationCount}");
                return Task.FromResult("这是摘要");
            };

            await manager.TranslateAsync(Png(1), CancellationToken.None);
            await manager.TranslateAsync(Png(2), CancellationToken.None);
            await manager.TranslateAsync(Png(3), CancellationToken.None);

            Assert.Equal("这是摘要", manager.Summary);
            Assert.Null(store.LoadStory("game.exe"));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Summary_disabled_does_not_generate_or_carry_summary()
    {
        var client = new RecordingClient();
        var translationCount = 0;
        client.Handler = messages =>
        {
            var last = messages[^1];
            if (last.ImagePng is not null)
                return Task.FromResult($"译文{++translationCount}");
            return Task.FromResult("这是摘要");
        };

        var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1) { SummaryEnabled = false };

        await manager.TranslateAsync(Png(1), CancellationToken.None);
        await manager.TranslateAsync(Png(2), CancellationToken.None);
        await manager.TranslateAsync(Png(3), CancellationToken.None); // 触发压缩，但短期记忆关闭

        Assert.Null(manager.Summary);

        var request = manager.BuildRequest(Png(4));
        Assert.DoesNotContain(request, m => m.Role == "system");
    }

    [Fact]
    public async Task Story_disabled_does_not_write_story()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenTranslatorStory_" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new RecordingClient();
            var store = new SummaryStore(dir);
            var manager = new ConversationManager(client, "翻译", maxTurns: 2, keepTurns: 1, storyStore: store) { StoryEnabled = false };
            manager.MemoryKey = "game.exe";

            var translationCount = 0;
            client.Handler = messages =>
            {
                var last = messages[^1];
                if (last.ImagePng is not null)
                    return Task.FromResult($"译文{++translationCount}");
                if (last.Text!.Contains("剧情梗概"))
                    return Task.FromResult("剧情补充：主角进入新区域");
                return Task.FromResult("这是摘要");
            };

            // 触发两次压缩（第 2 次本应写剧情），但 StoryEnabled=false
            await manager.TranslateAsync(Png(1), CancellationToken.None);
            await manager.TranslateAsync(Png(2), CancellationToken.None);
            await manager.TranslateAsync(Png(3), CancellationToken.None);
            await manager.TranslateAsync(Png(4), CancellationToken.None);
            await manager.TranslateAsync(Png(5), CancellationToken.None);

            Assert.Equal("这是摘要", manager.Summary);
            Assert.Null(store.LoadStory("game.exe"));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
