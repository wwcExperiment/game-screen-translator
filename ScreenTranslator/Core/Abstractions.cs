namespace ScreenTranslator.Core;

/// <summary>
/// 一帧捕获结果：<see cref="Signature"/> 是降采样灰度图（用于比对变化），
/// <see cref="PngBytes"/> 是完整截图 PNG（用于发送给模型）。
/// </summary>
public sealed record Frame(byte[] Signature, byte[] PngBytes);

/// <summary>捕获指定屏幕区域的一帧。</summary>
public interface IFrameSource
{
    Frame Capture();
}

/// <summary>把一条消息列表发给本地模型，返回文本（OpenAI 兼容 chat 底层接口）。</summary>
public interface ITranslationClient
{
    Task<string> TranslateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);

    /// <summary>总结类调用（短期摘要压缩、长期剧情梗概）专用入口；实现可用更高一级的模型。</summary>
    Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}

/// <summary>高层翻译入口：把一张截图翻译成文本（内部维护对话历史上下文）。</summary>
public interface ITranslator
{
    /// <summary>发送给模型的提示词，可在运行时由用户编辑（如追加「Sakura 保持原文」等备注）。</summary>
    string Prompt { get; set; }

    /// <summary>历史压缩后的长期摘要（未压缩时为 null）。</summary>
    string? Summary { get; }

    /// <summary>设置长期摘要（例如启动/切换窗口时从磁盘恢复长期记忆）。</summary>
    void SetSummary(string? summary);

    /// <summary>是否启用短期记忆（摘要压缩）。关闭后不生成、也不携带摘要。</summary>
    bool SummaryEnabled { get; set; }

    /// <summary>是否启用长期留档（剧情梗概写盘）。</summary>
    bool StoryEnabled { get; set; }

    /// <summary>剧情梗概的存档键（目标窗口进程名）；为 null 时不存剧情。</summary>
    string? MemoryKey { get; set; }

    /// <summary>长期摘要更新后触发（可能在后台线程）。</summary>
    event Action? SummaryChanged;

    Task<string> TranslateAsync(byte[] pngBytes, CancellationToken cancellationToken);
}
