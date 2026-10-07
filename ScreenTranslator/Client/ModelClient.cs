using System.Net.Http;
using System.Text;
using ScreenTranslator.Core;

namespace ScreenTranslator.Client;

public sealed class ModelClient : ITranslationClient
{
    private readonly HttpClient _http;
    private readonly string _endpointUrl;
    private volatile string _model;
    private volatile string _summarizeModel;
    private volatile string _apiKey;

    public ModelClient(HttpClient http, string endpointUrl, string model, string summarizeModel = "", string apiKey = "")
    {
        _http = http;
        _endpointUrl = endpointUrl;
        _model = model ?? "";
        _summarizeModel = summarizeModel ?? "";
        _apiKey = apiKey ?? "";
    }

    /// <summary>当前使用的翻译模型名；运行期可由 UI 切换（下次请求生效）。</summary>
    public string Model
    {
        get => _model;
        set => _model = value ?? "";
    }

    /// <summary>总结类调用所用模型；留空则跟随 <see cref="Model"/>（翻译模型）。</summary>
    public string SummarizeModel
    {
        get => _summarizeModel;
        set => _summarizeModel = value ?? "";
    }

    /// <summary>API Key（Bearer Token）；留空则不发送 Authorization 头。</summary>
    public string ApiKey
    {
        get => _apiKey;
        set => _apiKey = value ?? "";
    }

    public Task<string> TranslateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => SendAsync(messages, cancellationToken, _model);

    public Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => SendAsync(messages, cancellationToken, string.IsNullOrWhiteSpace(_summarizeModel) ? _model : _summarizeModel);

    private async Task<string> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken, string model)
    {
        var json = RequestBuilder.Build(model, messages, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var result = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;
            result.Append(SseParser.ExtractDelta(trimmed.Substring(5).Trim()));
        }

        return result.ToString();
    }
}
