using System.Text.Encodings.Web;
using System.Text.Json;
using ScreenTranslator.Core;

namespace ScreenTranslator.Client;

/// <summary>把消息列表拼成 OpenAI 兼容的 chat completions 请求体。</summary>
public static class RequestBuilder
{
    // 输出原始 UTF-8（不把中文转成 \uXXXX），便于阅读与调试。
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Build(string model, IReadOnlyList<ChatMessage> messages, bool stream)
    {
        var messageObjs = new List<object>(messages.Count);
        foreach (var m in messages)
        {
            if (m.ImagePng is not null)
            {
                messageObjs.Add(new
                {
                    role = m.Role,
                    content = new object[]
                    {
                        new { type = "text", text = m.Text ?? "" },
                        new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(m.ImagePng) } },
                    },
                });
            }
            else
            {
                messageObjs.Add(new { role = m.Role, content = m.Text ?? "" });
            }
        }

        var payload = new { model, messages = messageObjs, stream };
        return JsonSerializer.Serialize(payload, Options);
    }
}
