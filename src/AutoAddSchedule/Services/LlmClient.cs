using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// DeepSeek（OpenAI 兼容协议）客户端：POST {BaseUrl}/chat/completions，图片以 base64 data URI 传入。
/// 无参构造在运行时从 <see cref="AppServices.Config"/> 读取配置；测试可注入配置提供者与 HttpClient。
/// </summary>
public sealed class LlmClient : ILlmClient
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const int MaxErrorBodyLength = 300;

    private readonly Func<LlmConfig> _configProvider;
    private readonly HttpClient _httpClient;

    /// <summary>使用应用当前配置（AppServices.Config）与共享 HttpClient。</summary>
    public LlmClient()
        : this(null, null)
    {
    }

    /// <summary>可注入构造：配置提供者与 HttpClient 便于离线单元测试。</summary>
    public LlmClient(Func<LlmConfig>? configProvider = null, HttpClient? httpClient = null)
    {
        _configProvider = configProvider ?? DefaultConfigProvider;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <summary>当前配置中的模型名，用于界面展示与提取结果记录。</summary>
    public string ModelName => ResolveConfig().Model;

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = ResolveConfig();
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException("尚未配置大模型 API Key，请在设置中填写 DeepSeek API Key。");
        }

        var model = string.IsNullOrWhiteSpace(config.Model) ? "deepseek-flash" : config.Model.Trim();
        var timeoutSeconds = NormalizeTimeout(config.TimeoutSeconds);
        var endpoint = BuildEndpoint(config.BaseUrl);

        Dictionary<string, object?> payload;
        try
        {
            payload = BuildPayload(request, model);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("读取截图文件失败：" + ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("读取截图文件失败：" + ex.Message, ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var json = JsonSerializer.Serialize(payload, RequestJsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            AppLog.Info("调用大模型：POST " + endpoint + "，模型=" + model + "，图片=" + request.ImagePaths.Count + " 张。");

            using var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn("大模型请求失败：HTTP " + (int)response.StatusCode + "，" + Truncate(Flatten(body), MaxErrorBodyLength));
                throw new InvalidOperationException(BuildHttpErrorMessage(response.StatusCode, body));
            }

            return ExtractContent(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("无法连接大模型服务：请求超时（" + timeoutSeconds + " 秒），请检查网络后重试。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("无法连接大模型服务：请检查网络连接与 Base URL 配置。" + ex.Message, ex);
        }
    }

    public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var request = new LlmRequest
        {
            SystemPrompt = "你是一个连接测试助手。",
            UserPrompt = "请只回复：连接成功",
            JsonMode = false
        };

        await CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        var model = ResolveConfig().Model;
        return "连接成功（模型：" + (string.IsNullOrWhiteSpace(model) ? "未知" : model.Trim()) + "）";
    }

    private static Dictionary<string, object?> BuildPayload(LlmRequest request, string model)
    {
        var messages = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            }
        };

        object userContent;
        if (request.ImagePaths.Count == 0)
        {
            userContent = request.UserPrompt;
        }
        else
        {
            var parts = new List<object>();
            if (!string.IsNullOrWhiteSpace(request.UserPrompt))
            {
                parts.Add(new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = request.UserPrompt
                });
            }

            foreach (var path in request.ImagePaths)
            {
                parts.Add(new Dictionary<string, object?>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, object?>
                    {
                        ["url"] = BuildImageDataUri(path)
                    }
                });
            }

            userContent = parts;
        }

        messages.Add(new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["content"] = userContent
        });

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages
        };

        if (request.JsonMode)
        {
            payload["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "json_object"
            };
        }

        return payload;
    }

    /// <summary>读取本地图片并生成 base64 data URI。</summary>
    private static string BuildImageDataUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("图片路径不能为空。", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("截图文件不存在：" + path, path);
        }

        var bytes = File.ReadAllBytes(path);
        return "data:" + GetMimeType(path) + ";base64," + Convert.ToBase64String(bytes);
    }

    private static string GetMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }

    private static string ExtractContent(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("大模型服务返回了空响应，请稍后重试。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("大模型服务返回的响应不是合法 JSON。", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(root, "choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("大模型服务返回格式异常：缺少 choices 内容。");
            }

            var choice = choices[0];
            if (!TryGetProperty(choice, "message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("大模型服务返回格式异常：缺少 message 内容。");
            }

            if (!TryGetProperty(message, "content", out var contentElement) || contentElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("大模型返回内容为空，请稍后重试。");
            }

            var content = contentElement.GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("大模型返回内容为空，请稍后重试。");
            }

            return content;
        }
    }

    private static string BuildHttpErrorMessage(HttpStatusCode statusCode, string body)
    {
        var code = (int)statusCode;
        if (code == 401)
        {
            return "大模型 API Key 无效或已过期，请在设置中重新填写。";
        }

        if (code == 403)
        {
            return "大模型 API Key 无权访问该服务（HTTP 403），请检查 Key 与 Base URL 配置。";
        }

        if (code == 429)
        {
            return "请求过于频繁（HTTP 429），请稍后重试。";
        }

        var detail = Truncate(Flatten(body), MaxErrorBodyLength);
        return "大模型服务返回错误（HTTP " + code + " " + statusCode + "）：" + detail;
    }

    private static string BuildEndpoint(string? baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.deepseek.com"
            : baseUrl.Trim().TrimEnd('/');

        return value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? value
            : value + "/chat/completions";
    }

    private static int NormalizeTimeout(int seconds) => Math.Clamp(seconds <= 0 ? 120 : seconds, 5, 600);

    private static string Flatten(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "…（已截断）";
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static LlmConfig DefaultConfigProvider()
    {
        try
        {
            var store = AppServices.Config;
            return store is null ? new LlmConfig() : store.Config.Llm;
        }
        catch
        {
            return new LlmConfig();
        }
    }

    private LlmConfig ResolveConfig() => _configProvider() ?? new LlmConfig();
}
