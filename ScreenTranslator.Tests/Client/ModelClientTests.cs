using System.Net;
using System.Text;
using ScreenTranslator.Client;
using ScreenTranslator.Core;

namespace ScreenTranslator.Tests.Client;

public sealed class ModelClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _impl;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> impl) => _impl = impl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _impl(request, cancellationToken);
    }

    private static List<ChatMessage> SampleMessages() => new()
    {
        new() { Role = "system", Text = "【此前对话摘要】\n游戏对话" },
        new() { Role = "user", Text = "翻译", ImagePng = new byte[] { 1, 2, 3 } },
    };

    [Fact]
    public async Task TranslateAsync_posts_correct_body_and_parses_stream()
    {
        string? uri = null;
        string? body = null;

        var handler = new FakeHandler(async (req, ct) =>
        {
            uri = req.RequestUri!.ToString();
            body = await req.Content!.ReadAsStringAsync(ct);
            var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
                      "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
                      "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new ModelClient(http, "http://127.0.0.1:1234/v1/chat/completions", "m");

        var text = await client.TranslateAsync(SampleMessages(), CancellationToken.None);

        Assert.Equal("http://127.0.0.1:1234/v1/chat/completions", uri);
        Assert.Equal("你好", text);
        Assert.Contains("data:image/png;base64,", body!);
        Assert.Contains("翻译", body!);
        Assert.Contains("【此前对话摘要】", body!);
    }

    [Fact]
    public async Task TranslateAsync_propagates_cancellation()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });

        using var http = new HttpClient(handler);
        var client = new ModelClient(http, "http://x/v1/chat/completions", "m");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.TranslateAsync(SampleMessages(), cts.Token));
    }

    [Fact]
    public async Task TranslateAsync_sends_bearer_header_when_api_key_set()
    {
        string? auth = null;

        var handler = new FakeHandler(async (req, ct) =>
        {
            auth = req.Headers.Authorization?.ToString();
            var sse = "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new ModelClient(http, "http://x/v1/chat/completions", "m", apiKey: "sk-test-123");

        await client.TranslateAsync(SampleMessages(), CancellationToken.None);

        Assert.Equal("Bearer sk-test-123", auth);
    }

    [Fact]
    public async Task TranslateAsync_sends_no_authorization_header_when_api_key_empty()
    {
        string? auth = "unset";

        var handler = new FakeHandler(async (req, ct) =>
        {
            auth = req.Headers.Authorization?.ToString();
            var sse = "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new ModelClient(http, "http://x/v1/chat/completions", "m");

        await client.TranslateAsync(SampleMessages(), CancellationToken.None);

        Assert.Null(auth);
    }
}
