using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTools.Plugins;

public class OpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl = "http://127.0.0.1:11434/v1/chat/completions"; // 或其他兼容地址

    public OpenAIService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task TranslateAsync(string text,
        string targetLang,
        IProgress<string> progress,
        CancellationToken ct = default, 
        string? model = "qwen3:1.7b")
    {
        var request = new OpenAIRequest
        {
            Model = model,
            Messages = new List<Message>
            {
                new()
                {
                    Role = "user",
                    Content = $"/nothink Translate the following text into {targetLang}: {text}"
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _apiUrl)
        {
            Content = content
        };
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"API Error: {await response.Content.ReadAsStringAsync()}");

        // 读取流式响应
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        string? buffer;
        while ((buffer = await reader.ReadLineAsync()) is not null && !ct.IsCancellationRequested)
        {
            buffer = buffer.Trim();
            if (buffer.StartsWith("data: ") && buffer != "data: [DONE]")
            {
                var jsonStr = buffer.Substring(6);
                try
                {
                    var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(jsonStr);
                    if (chunk?.Choices?.Length > 0)
                    {
                        var delta = chunk.Choices[0].Delta?.Content;
                        if (!string.IsNullOrEmpty(delta))
                        {
                            progress?.Report(delta); // 实时推送
                        }
                    }
                }
                catch (JsonException) { /* 忽略无效chunk */ }
            }
        }
    }
}

public class OpenAIRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; } = "qwen3:8b";

    [JsonPropertyName("messages")]
    public List<Message>? Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = true;
}

public class Message
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class OpenAIStreamChunk
{
    [JsonPropertyName("choices")]
    public StreamChoice[]? Choices { get; set; }
}

public class StreamChoice
{
    [JsonPropertyName("delta")]
    public Delta? Delta { get; set; }
}

public class Delta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}