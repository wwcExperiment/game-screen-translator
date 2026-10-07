namespace ScreenTranslator.Core;

/// <summary>
/// 文本相似度工具：判断两次翻译内容是否「几乎一样」（用于触发超时退避）。
/// 先归一化（去空白/标点/符号、转小写）消除格式差异，再按编辑距离计算相似度。
/// </summary>
public static class TextSimilarity
{
    /// <summary>归一化文本：去掉空白、标点、符号并转小写，用于比较实质内容。</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>归一化后的相似度（0~1）。两个空串视为完全相同（1.0）。</summary>
    public static double Similarity(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 && nb.Length == 0)
            return 1.0;
        if (na.Length == 0 || nb.Length == 0)
            return 0.0;

        var dist = LevenshteinDistance(na, nb);
        return 1.0 - (double)dist / Math.Max(na.Length, nb.Length);
    }

    /// <summary>判断两段文本是否「几乎一样」（相似度达到阈值）。</summary>
    public static bool AreNearlySame(string? a, string? b, double threshold = 0.8) =>
        Similarity(a, b) >= threshold;

    private static int LevenshteinDistance(string a, string b)
    {
        // 让 a 为较短者，减小空间占用。
        if (a.Length > b.Length)
            (a, b) = (b, a);

        var prev = new int[a.Length + 1];
        var curr = new int[a.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            prev[i] = i;

        for (var j = 1; j <= b.Length; j++)
        {
            curr[0] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[i] = Math.Min(
                    Math.Min(curr[i - 1] + 1, prev[i] + 1),
                    prev[i - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[a.Length];
    }
}
