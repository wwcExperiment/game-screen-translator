using ScreenTranslator.Client;

namespace ScreenTranslator.Tests.Client;

public sealed class SseParserTests
{
    [Fact]
    public void ExtractDelta_returns_content()
    {
        Assert.Equal("你好", SseParser.ExtractDelta("""{"choices":[{"delta":{"content":"你好"}}]}"""));
    }

    [Fact]
    public void ExtractDelta_returns_empty_when_no_content()
    {
        Assert.Equal("", SseParser.ExtractDelta("""{"choices":[{"delta":{"role":"assistant"}}]}"""));
        Assert.Equal("", SseParser.ExtractDelta("[DONE]"));
    }

    [Fact]
    public void ExtractFullText_accumulates_deltas()
    {
        var raw = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
                  "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
                  "data: [DONE]\n\n";
        Assert.Equal("你好", SseParser.ExtractFullText(raw));
    }
}
