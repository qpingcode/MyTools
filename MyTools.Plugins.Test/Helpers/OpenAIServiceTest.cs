using System.Text;
using MyTools.Plugins;
using NUnit.Framework;

[TestFixture]
public class OpenAIServiceTest
{
    private HttpClient? _httpClient;
    
    [SetUp]
    public void Setup()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
    }

    [Test]
    public async Task TranslateAsync_WithStreamResponse_ShouldUpdateProgressCorrectly()
    {
        // Arrange: 设置预期的SSE流响应
        var expectedContent = "Hello World!";
        var openAIService = new OpenAIService(_httpClient!);
        var progressResults = new List<string>();
        var progress = new Progress<string>(s =>
        {
            progressResults.Add(s);
            Console.WriteLine(s);
        });

        // Act
        await openAIService.TranslateAsync("Today is a good day", "chinese", progress);

        // Assert: 验证progressResults是否包含了正确的翻译结果
        Assert.That(progressResults.Count, Is.GreaterThan(0), "Progress results should not be empty.");
        Assert.That(string.Concat(progressResults), Is.EqualTo(expectedContent), "The concatenated progress results do not match the expected content.");
    }

    private MemoryStream GenerateMockSseStream(string content)
    {
        // 创建一个模拟的SSE格式响应流
        var sseChunks = content.Select(c => $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{c}\"}}}}]}}\n\n").ToList();
        var streamContent = string.Join("", sseChunks) + "data: [DONE]\n\n";
        return new MemoryStream(Encoding.UTF8.GetBytes(streamContent));
    }
}