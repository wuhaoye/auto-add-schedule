using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>LlmClient 的离线单元测试：请求构造、响应解析与错误映射（使用假 HttpMessageHandler，不联网）。</summary>
public class LlmClientTests
{
    private const string ValidResponse =
        "{\"id\":\"chatcmpl-test\",\"object\":\"chat.completion\",\"model\":\"deepseek-flash\"," +
        "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"title\\\":\\\"测试日程\\\"}\"},\"finish_reason\":\"stop\"}]}";

    [Fact]
    public async Task CompleteAsync_Should_Post_OpenAI_Compatible_Request()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(ValidResponse));
        var client = CreateClient(handler);
        var imagePath = CreateTempImage();

        try
        {
            var content = await client.CompleteAsync(new LlmRequest
            {
                SystemPrompt = "系统提示",
                UserPrompt = "用户提示",
                ImagePaths = new[] { imagePath },
                JsonMode = true
            });

            Assert.Equal("{\"title\":\"测试日程\"}", content);
            Assert.Equal("https://api.example.com/chat/completions", handler.LastRequestUri);
            Assert.Equal("Bearer test-key", handler.LastAuthorization);
            Assert.NotNull(handler.LastBody);

            using var document = JsonDocument.Parse(handler.LastBody!);
            var root = document.RootElement;
            Assert.Equal("deepseek-flash", root.GetProperty("model").GetString());
            Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());

            var messages = root.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("用户提示", messages[1].GetProperty("content")[0].GetProperty("text").GetString());

            var imagePart = messages[1].GetProperty("content")[1];
            Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
            var dataUri = imagePart.GetProperty("image_url").GetProperty("url").GetString();
            Assert.StartsWith("data:image/png;base64,", dataUri);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task CompleteAsync_Without_Images_Should_Send_Text_Only_And_No_Json_Mode()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(ValidResponse));
        var client = CreateClient(handler);

        await client.CompleteAsync(new LlmRequest
        {
            SystemPrompt = "系统提示",
            UserPrompt = "只有文字",
            JsonMode = false
        });

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("response_format", out _));
        var message = root.GetProperty("messages")[1];
        Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
        Assert.Equal("只有文字", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_Should_Throw_Chinese_Error_When_ApiKey_Missing()
    {
        var config = new LlmConfig { ApiKey = string.Empty };
        var client = new LlmClient(() => config, new HttpClient(new StubHttpHandler(_ => JsonResponse(ValidResponse))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteAsync(TextRequest()));

        Assert.Contains("API Key", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Map_401_To_Chinese_Key_Error()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(
            "{\"error\":{\"message\":\"invalid api key\"}}",
            HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("API Key 无效或已过期", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Map_429_To_Frequency_Error()
    {
        var handler = new StubHttpHandler(_ => JsonResponse("{\"error\":\"rate limit\"}", (HttpStatusCode)429));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("请求过于频繁", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Map_Other_Http_Error_With_Status_Code()
    {
        var handler = new StubHttpHandler(_ => JsonResponse("模型不存在", HttpStatusCode.BadRequest));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("400", ex.Message);
        Assert.Contains("模型不存在", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Map_Connection_Failure_To_Chinese_Error()
    {
        var handler = new ThrowingHttpHandler();
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("无法连接大模型服务", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Map_Timeout_To_Chinese_Error()
    {
        var httpClient = new HttpClient(new HangingHttpHandler()) { Timeout = TimeSpan.FromMilliseconds(200) };
        var client = new LlmClient(() => CreateConfig(), httpClient);

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("无法连接大模型服务", ex.Message);
        Assert.Contains("超时", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Should_Throw_When_Content_Empty()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(TextRequest()));

        Assert.Contains("内容为空", ex.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_Should_Return_Model_Name()
    {
        var handler = new StubHttpHandler(_ => JsonResponse(ValidResponse));
        var client = CreateClient(handler);

        var message = await client.TestConnectionAsync();

        Assert.Contains("连接成功", message);
        Assert.Contains("deepseek-flash", message);
    }

    private static LlmRequest TextRequest() => new()
    {
        SystemPrompt = "系统提示",
        UserPrompt = "用户提示",
        JsonMode = true
    };

    private static LlmConfig CreateConfig() => new()
    {
        ApiKey = "test-key",
        BaseUrl = "https://api.example.com",
        Model = "deepseek-flash",
        TimeoutSeconds = 30
    };

    private static LlmClient CreateClient(HttpMessageHandler handler) =>
        new(() => CreateConfig(), new HttpClient(handler));

    private static string CreateTempImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoAddSchedule-LlmTests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        return path;
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public string? LastRequestUri { get; private set; }

        public string? LastAuthorization { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("找不到主机");
    }

    private sealed class HangingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("不可达。");
        }
    }
}
