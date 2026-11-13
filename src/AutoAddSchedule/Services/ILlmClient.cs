namespace AutoAddSchedule.Services;

/// <summary>一次大模型请求：系统提示词 + 用户提示词 + 可选本地图片。</summary>
public sealed class LlmRequest
{
    public string SystemPrompt { get; init; } = string.Empty;

    public string UserPrompt { get; init; } = string.Empty;

    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>是否要求模型返回 JSON 对象。</summary>
    public bool JsonMode { get; init; } = true;
}

public interface ILlmClient
{
    /// <summary>发送请求并返回模型输出的文本内容（OpenAI 兼容 /chat/completions）。</summary>
    Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>测试连接：发送一次最小请求，成功返回模型名或简短说明。</summary>
    Task<string> TestConnectionAsync(CancellationToken cancellationToken = default);
}
