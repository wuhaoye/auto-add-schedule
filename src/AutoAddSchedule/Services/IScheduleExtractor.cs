using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

public interface IScheduleExtractor
{
    /// <summary>
    /// 把截图（可选补充信息）交给大模型，提取出结构化日程草稿。
    /// </summary>
    Task<ExtractionResult> ExtractAsync(string imagePath, string? supplement, CancellationToken cancellationToken = default);
}
