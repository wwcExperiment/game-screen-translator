namespace ScreenTranslator.Core;

/// <summary>
/// 一条聊天消息。Role 为 system / user / assistant。
/// 文本消息：<see cref="Text"/> 非空、<see cref="ImagePng"/> 为空；图片消息反之。
/// </summary>
public sealed record ChatMessage
{
    public string Role { get; init; } = "user";
    public string? Text { get; init; }
    public byte[]? ImagePng { get; init; }
}
