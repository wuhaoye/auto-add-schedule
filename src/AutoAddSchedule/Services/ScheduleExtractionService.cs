using System.IO;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 截图 → 结构化日程：构造提示词（注入当前日期）、调用大模型、解析并校验 JSON。
/// 失败时抛出中文异常；解析失败会把原始返回记录到日志（截断，且不含密钥）。
/// </summary>
public sealed class ScheduleExtractionService : IScheduleExtractor
{
    private const int MaxLoggedRawLength = 2000;

    private readonly ILlmClient _llmClient;
    private readonly Func<DateTime> _nowProvider;
    private readonly Func<string?> _modelProvider;

    /// <summary>使用默认 <see cref="LlmClient"/> 与本地当前时间。</summary>
    public ScheduleExtractionService()
        : this(new LlmClient(), null, null)
    {
    }

    /// <summary>可注入构造：便于离线单元测试。</summary>
    public ScheduleExtractionService(ILlmClient llmClient, Func<DateTime>? nowProvider = null, Func<string?>? modelProvider = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _nowProvider = nowProvider ?? (() => DateTime.Now);
        _modelProvider = modelProvider ?? (() => (llmClient as LlmClient)?.ModelName);
    }

    public async Task<ExtractionResult> ExtractAsync(string imagePath, string? supplement, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("截图路径不能为空。", nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("要识别的截图不存在：" + imagePath, imagePath);
        }

        var now = _nowProvider();
        var request = new LlmRequest
        {
            SystemPrompt = LlmPrompt.BuildSystemPrompt(now),
            UserPrompt = LlmPrompt.BuildUserPrompt(supplement),
            ImagePaths = new[] { imagePath },
            JsonMode = true
        };

        var raw = await _llmClient.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        var model = _modelProvider();
        AppLog.Info("大模型日程提取完成，模型：" + (model ?? "未知") + "，返回长度：" + (raw?.Length ?? 0));

        try
        {
            var draft = ScheduleJsonParser.Parse(raw);
            return new ExtractionResult
            {
                Draft = draft,
                RawJson = raw ?? string.Empty,
                Model = model
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error("大模型返回的日程内容解析失败，原始返回：" + Truncate(raw, MaxLoggedRawLength), ex);
            throw new InvalidOperationException("日程解析失败：" + ex.Message, ex);
        }
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "…（已截断）";
    }
}
