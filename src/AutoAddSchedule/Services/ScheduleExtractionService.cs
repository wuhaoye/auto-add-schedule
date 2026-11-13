using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 截图 → 结构化日程。由大模型模块实现（提示词 + JSON 解析 + 校验）。
/// </summary>
public sealed class ScheduleExtractionService : IScheduleExtractor
{
    public Task<ExtractionResult> ExtractAsync(string imagePath, string? supplement, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("日程提取服务尚未实现。");
    }
}
