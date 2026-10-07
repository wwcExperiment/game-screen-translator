using System.Net;
using System.Text;
using ScreenTranslator.Client;

namespace ScreenTranslator.Tests.Client;

public sealed class ModelDiscoveryTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _impl;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> impl) => _impl = impl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _impl(request, cancellationToken);
    }

    private static FakeHandler OkHandler(Action<string> onUrl) => new(async (req, ct) =>
    {
        onUrl(req.RequestUri!.ToString());
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"id\":\"m\"}]}", Encoding.UTF8, "application/json"),
        };
    });

    [Theory]
    [InlineData("http://x:1/v1/chat/completions", "http://x:1/v1/models")]
    [InlineData("http://x:1/proxy/v1/chat/completions", "http://x:1/proxy/v1/models")]
    [InlineData("http://x:1/v1/completions", "http://x:1/v1/models")]
    [InlineData("http://x:1/xxxEndPoint", "http://x:1/xxxEndPoint/v1/models")]
    public async Task ListModelsAsync_derives_models_url_from_endpoint(string endpoint, string expectedModelsUrl)
    {
        string? requested = null;
        using var http = new HttpClient(OkHandler(u => requested = u));

        var models = await ModelDiscovery.ListModelsAsync(http, endpoint);

        Assert.Equal(expectedModelsUrl, requested);
        Assert.Single(models);
    }
}
