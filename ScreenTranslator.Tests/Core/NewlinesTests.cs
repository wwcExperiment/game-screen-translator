using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Core;

public sealed class NewlinesTests
{
    [Fact]
    public void ToReal_converts_literal_newline_to_crlf()
    {
        Assert.Equal("你好\r\n世界", Newlines.ToReal("你好\\n世界"));
    }

    [Fact]
    public void ToReal_normalizes_lf_to_crlf()
    {
        Assert.Equal("你好\r\n世界", Newlines.ToReal("你好\n世界"));
    }

    [Fact]
    public void ToReal_normalizes_crlf_idempotently()
    {
        Assert.Equal("你好\r\n世界", Newlines.ToReal("你好\r\n世界"));
    }

    [Fact]
    public void ToReal_normalizes_cr_to_crlf()
    {
        Assert.Equal("你好\r\n世界", Newlines.ToReal("你好\r世界"));
    }

    [Fact]
    public void ToLiteral_converts_real_newline_to_literal()
    {
        Assert.Equal("你好\\n世界", Newlines.ToLiteral("你好\n世界"));
    }

    [Fact]
    public void ToLiteral_handles_crlf()
    {
        Assert.Equal("你好\\n世界", Newlines.ToLiteral("你好\r\n世界"));
    }

    [Fact]
    public void Null_or_empty_returns_empty()
    {
        Assert.Equal("", Newlines.ToReal(null));
        Assert.Equal("", Newlines.ToLiteral(null));
        Assert.Equal("", Newlines.ToReal(""));
        Assert.Equal("", Newlines.ToLiteral(""));
    }
}
