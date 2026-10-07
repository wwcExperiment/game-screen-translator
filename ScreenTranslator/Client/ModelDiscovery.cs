using System.Net.Http;
using System.Text.Json;

namespace ScreenTranslator.Client;

/// <summary>通过 <c>/v1/models</c> 列出服务端可用的模型名。</summary>
public static class ModelDiscovery
{
    public static async Task<List<string>> ListModelsAsync(HttpClient http, string endpointUrl, string? apiKey = null, CancellationToken cancellationToken = default)
    {
        var url = DeriveModelsUrl(endpointUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = "";
            try { detail = await response.Content.ReadAsStringAsync(cancellationToken); }
            catch { }
            if (detail.Length > 500) detail = detail[..500];
            throw new HttpRequestException($"模型列表请求失败：GET {url} -> HTTP {(int)response.StatusCode} {response.ReasonPhrase}；响应内容：{detail}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var result = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } s)
                result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// 从 chat completions 端点推导 /v1/models 地址，保留可能的路径前缀：
    /// .../v1/chat/completions -> .../v1/models；不以 completions 结尾时视为 base URL 直接拼 /v1/models。
    /// </summary>
    private static string DeriveModelsUrl(string endpointUrl)
    {
        var trimmed = endpointUrl.TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/chat/completions".Length] + "/models";
        if (trimmed.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^"/completions".Length] + "/models";
        return trimmed + "/v1/models";
    }
}
