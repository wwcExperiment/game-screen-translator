using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class TranslationSplitterTests
{
    [Fact]
    public void Splits_both_labels_with_colon()
    {
        var r = TranslationSplitter.Split("原文：hello\n译文：你好");
        Assert.Equal("hello", r.Original);
        Assert.Equal("你好", r.Translation);
    }

    [Fact]
    public void Splits_labels_on_separate_lines()
    {
        var r = TranslationSplitter.Split("原文：\nhello\n译文：\n你好");
        Assert.Equal("hello", r.Original);
        Assert.Equal("你好", r.Translation);
    }

    [Fact]
    public void Preserves_multiline_content()
    {
        var r = TranslationSplitter.Split("原文：line1\nline2\n译文：译1\n译2");
        Assert.Equal("line1\nline2", r.Original);
        Assert.Equal("译1\n译2", r.Translation);
    }

    [Fact]
    public void Accepts_ascii_colon()
    {
        var r = TranslationSplitter.Split("原文: hello\n译文: 你好");
        Assert.Equal("hello", r.Original);
        Assert.Equal("你好", r.Translation);
    }

    [Fact]
    public void Whole_text_is_translation_when_no_label()
    {
        var r = TranslationSplitter.Split("你好");
        Assert.Equal("", r.Original);
        Assert.Equal("你好", r.Translation);
    }

    [Fact]
    public void Only_translation_label_yields_empty_original()
    {
        var r = TranslationSplitter.Split("译文：你好");
        Assert.Equal("", r.Original);
        Assert.Equal("你好", r.Translation);
    }

    [Fact]
    public void Null_or_empty_yields_empty_result()
    {
        Assert.Equal(new TranslationResult("", ""), TranslationSplitter.Split(null));
        Assert.Equal(new TranslationResult("", ""), TranslationSplitter.Split(""));
        Assert.Equal(new TranslationResult("", ""), TranslationSplitter.Split("原文：\n译文："));
    }

    [Theory]
    [InlineData("无文字")]
    [InlineData("没有文字")]
    [InlineData("无文本")]
    [InlineData("无文字。")]
    [InlineData("图片中没有文字")]
    [InlineData("No text")]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("（无文字）")]
    [InlineData("(无文字)")]
    [InlineData("（无可翻译内容）")]
    [InlineData("（无文字可译）")]
    [InlineData("（无可见文字）")]
    [InlineData("（没有文字）")]
    [InlineData("（无文本。）")]
    [InlineData("无")]
    [InlineData("（无）")]
    [InlineData("无文字区域")]
    [InlineData("（无文字区域）")]
    [InlineData("无可见文本区域")]
    public void IsNoText_returns_true_for_no_text_markers(string? text)
    {
        Assert.True(TranslationSplitter.IsNoText(text));
    }

    [Theory]
    [InlineData("你好")]
    [InlineData("hello world")]
    [InlineData("无文字说明")]
    public void IsNoText_returns_false_for_real_translations(string? text)
    {
        Assert.False(TranslationSplitter.IsNoText(text));
    }

    [Theory]
    [InlineData("无文字")]
    [InlineData("（无可翻译内容）")]
    [InlineData("无文字可译")]
    public void MatchesNoTextMarker_returns_true_for_markers(string? text)
    {
        Assert.True(TranslationSplitter.MatchesNoTextMarker(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("你好")]
    [InlineData("无文字说明")]
    public void MatchesNoTextMarker_returns_false_for_non_markers(string? text)
    {
        Assert.False(TranslationSplitter.MatchesNoTextMarker(text));
    }

    [Fact]
    public void IsNoText_result_is_true_when_either_side_is_a_marker()
    {
        Assert.True(TranslationSplitter.IsNoText(
            TranslationSplitter.Split("原文：（无可见文字）\n译文：（图片内容为纯深色背景，无任何可识别的文字信息。）")));
        Assert.True(TranslationSplitter.IsNoText(TranslationSplitter.Split("原文：你好\n译文：无文字")));
        Assert.False(TranslationSplitter.IsNoText(TranslationSplitter.Split("原文：你好\n译文：Hello")));
    }
}
