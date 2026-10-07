namespace ScreenTranslator.Core;

/// <summary>模型返回的「原文 + 译文」解析结果。</summary>
public sealed record TranslationResult(string Original, string Translation);

/// <summary>
/// 把模型按「原文：/ 译文：」标签返回的结构化文本拆分成原文和译文两部分。
/// 对格式做容错：标签可带中文或英文冒号，内容可多行；若模型没有按格式输出，则整体当作译文。
/// </summary>
public static class TranslationSplitter
{
    private const string OriginalLabel = "原文";
    private const string TranslationLabel = "译文";

    public static TranslationResult Split(string? raw)
    {
        var text = raw ?? string.Empty;
        var origIdx = IndexOfLabel(text, OriginalLabel);
        var transIdx = IndexOfLabel(text, TranslationLabel);

        string original;
        string translation;

        if (transIdx >= 0)
        {
            var start = SkipColonAndWhitespace(text, transIdx + TranslationLabel.Length);
            translation = text[start..].Trim();
        }
        else
        {
            translation = string.Empty;
        }

        if (origIdx >= 0)
        {
            var start = SkipColonAndWhitespace(text, origIdx + OriginalLabel.Length);
            var end = transIdx > origIdx ? transIdx : text.Length;
            original = text[start..end].Trim();
        }
        else
        {
            original = string.Empty;
        }

        // 没有任何标签时，整体当作译文（模型未按要求输出格式）。
        if (origIdx < 0 && transIdx < 0)
            translation = text.Trim();

        return new TranslationResult(original, translation);
    }

    /// <summary>
    /// 判断译文是否为「无文字」之类的占位说明。小模型在图片没有文字时，
    /// 未必严格按提示词输出空行，而可能直接返回「无文字」「没有文字」等字样，需按无文字处理。
    /// </summary>
    public static bool IsNoText(string? translation)
    {
        var normalized = TextSimilarity.Normalize(translation);
        return normalized.Length == 0 || MatchesNoTextMarker(normalized);
    }

    /// <summary>只判断是否命中「无文字」占位词；空字符串不算命中（空原文可能只是没有「原文：」标签）。</summary>
    public static bool MatchesNoTextMarker(string? text)
    {
        var normalized = TextSimilarity.Normalize(text);
        foreach (var marker in NoTextMarkers)
        {
            if (normalized == marker)
                return true;
        }
        return false;
    }

    /// <summary>判断一次翻译输出整体是否为「无文字」：译文为空或命中占位，或原文命中占位。</summary>
    public static bool IsNoText(TranslationResult result) =>
        IsNoText(result.Translation) || MatchesNoTextMarker(result.Original);

    private static readonly string[] NoTextMarkers =
    {
        "无文字", "没有文字", "无文本", "没有文本", "无内容", "没有内容",
        "无文字区域", "没有文字区域",
        "无可见文字", "没有可见文字", "无可见文本区域", "无文字可译", "无可翻译内容",
        "图中无文字", "图片中无文字", "图片里没有文字", "图片中没有文字",
        "未检测到文字", "无检测到文字", "没有检测到文字", "空白", "无", "无字",
        "无可翻译", "没有可翻译", "无翻译内容", "没有翻译内容",
        "notext", "notextfound", "notextdetected", "none", "nothing", "empty",
    };

    private static int IndexOfLabel(string text, string label)
    {
        // 优先匹配「标签 + 冒号」，避免把正文里的「原文/译文」误判成标签。
        var withCnColon = text.IndexOf(label + "：", StringComparison.Ordinal);
        if (withCnColon >= 0) return withCnColon;

        var withAsciiColon = text.IndexOf(label + ":", StringComparison.Ordinal);
        if (withAsciiColon >= 0) return withAsciiColon;

        return text.IndexOf(label, StringComparison.Ordinal);
    }

    private static int SkipColonAndWhitespace(string text, int idx)
    {
        while (idx < text.Length &&
               (text[idx] == '：' || text[idx] == ':' || char.IsWhiteSpace(text[idx])))
            idx++;
        return idx;
    }
}
