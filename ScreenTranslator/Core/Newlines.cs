namespace ScreenTranslator.Core;

/// <summary>换行符两种表示互转：真实换行（主文本框显示）与字面「\n」（历史框显示）。</summary>
public static class Newlines
{
    /// <summary>把字面「\n」（反斜杠+n）转成真实换行，用于主文本框显示。
    /// WinForms 多行 TextBox 只有 CRLF（\r\n）才会渲染成换行，单独的 \n 会被显示成一行，所以最终统一为 \r\n。</summary>
    public static string ToReal(string? s) =>
        (s ?? "")
            .Replace("\\n", "\n")   // 字面「\n」→ 真实换行
            .Replace("\r\n", "\n")   // CRLF → LF 统一
            .Replace("\r", "\n")     // CR → LF 统一
            .Replace("\n", "\r\n");  // LF → CRLF（供 TextBox 显示换行）

    /// <summary>把真实换行（\r\n、\r、\n）统一转成字面「\n」，用于历史框显示。</summary>
    public static string ToLiteral(string? s) =>
        (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
}
