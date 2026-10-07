using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class TextSimilarityTests
{
    [Fact]
    public void Normalize_removes_whitespace_punctuation_and_case()
    {
        Assert.Equal("helloworld", TextSimilarity.Normalize("Hello, World!"));
        Assert.Equal("你好世界", TextSimilarity.Normalize(" 你好，世界！ "));
        Assert.Equal("", TextSimilarity.Normalize(null));
        Assert.Equal("", TextSimilarity.Normalize("   "));
    }

    [Fact]
    public void Identical_texts_are_nearly_same()
    {
        Assert.True(TextSimilarity.AreNearlySame("译文：你好", "译文：你好"));
        Assert.True(TextSimilarity.AreNearlySame("你好，世界！", "你好 世界"));
    }

    [Fact]
    public void Different_texts_are_not_nearly_same()
    {
        Assert.False(TextSimilarity.AreNearlySame("完全相同", "相同内容"));
        Assert.False(TextSimilarity.AreNearlySame("", "你好"));
    }

    [Fact]
    public void Empty_texts_are_nearly_same()
    {
        Assert.True(TextSimilarity.AreNearlySame("", ""));
        Assert.True(TextSimilarity.AreNearlySame(null, "   "));
    }
}
