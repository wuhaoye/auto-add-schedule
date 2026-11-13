using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// DeepSeek（OpenAI 兼容协议）客户端。由大模型模块实现。
/// </summary>
public sealed class LlmClient : ILlmClient
{
    public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("大模型客户端尚未实现（待接入 DeepSeek /chat/completions）。");
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("大模型客户端尚未实现。");
    }
}
