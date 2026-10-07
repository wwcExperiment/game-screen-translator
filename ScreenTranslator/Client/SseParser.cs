using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Client;

/// <summary>解析 OpenAI 兼容的 SSE（server-sent events）流式响应。</summary>
public static class SseParser
{
    /// <summary>从单条 <c>data:</c> 载荷里提取 <c>choices[0].delta.content</c>；没有则返回空串。</summary>
    public static string ExtractDelta(string dataLine)
    {
        var json = dataLine.Trim();
        if (json.Length == 0 || json == "[DONE]")
            return string.Empty;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return string.Empty;

        var delta = choices[0].GetProperty("delta");
        if (delta.ValueKind != JsonValueKind.Object)
            return string.Empty;
        if (!delta.TryGetProperty("content", out var content))
            return string.Empty;

        return content.ValueKind == JsonValueKind.String ? content.GetString()! : string.Empty;
    }

    /// <summary>把整段原始 SSE 文本拼成完整译文（按行扫描 <c>data:</c>）。</summary>
    public static string ExtractFullText(string rawResponse)
    {
        var sb = new StringBuilder();
        foreach (var line in rawResponse.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;
            sb.Append(ExtractDelta(trimmed.Substring(5).Trim()));
        }
        return sb.ToString();
    }
}
