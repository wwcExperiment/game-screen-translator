using System.Text.Json;
using ScreenTranslator.Client;
using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Client;

public sealed class RequestBuilderTests
{
    [Fact]
    public void Build_image_message_produces_text_and_image_url_parts()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Text = "翻译", ImagePng = png },
        };
        var json = RequestBuilder.Build("my-model", messages, stream: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("my-model", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());

        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());

        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("翻译", content[0].GetProperty("text").GetString());

        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        var url = content[1].GetProperty("image_url").GetProperty("url").GetString();
        Assert.StartsWith("data:image/png;base64,", url);
        var b64 = url!.Substring("data:image/png;base64,".Length);
        Assert.Equal(png, Convert.FromBase64String(b64));
    }

    [Fact]
    public void Build_text_only_message_produces_plain_string_content()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Text = "摘要" },
            new() { Role = "assistant", Text = "你好" },
        };
        var json = RequestBuilder.Build("m", messages, stream: true);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("system", arr[0].GetProperty("role").GetString());
        Assert.Equal("摘要", arr[0].GetProperty("content").GetString());
        Assert.Equal("assistant", arr[1].GetProperty("role").GetString());
        Assert.Equal("你好", arr[1].GetProperty("content").GetString());
    }
}
