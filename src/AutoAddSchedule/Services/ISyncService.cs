using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

public sealed class SyncOutcome
{
    public bool Synced { get; init; }

    public string? Error { get; init; }

    public string Message { get; init; } = string.Empty;

    public static SyncOutcome Success(string message) => new() { Synced = true, Message = message };

    public static SyncOutcome Failure(string error) => new() { Synced = false, Error = error };
}

public interface ISyncService
{
    /// <summary>把单条日程推送（创建或更新）到 iCloud 日历。</summary>
    Task<SyncOutcome> PushAsync(ScheduleItem item, CancellationToken cancellationToken = default);

    /// <summary>删除 iCloud 上的对应事件。</summary>
    Task<SyncOutcome> DeleteRemoteAsync(ScheduleItem item, CancellationToken cancellationToken = default);

    /// <summary>尝试推送全部未同步日程（含待删除项），返回成功条数。</summary>
    Task<int> PushPendingAsync(CancellationToken cancellationToken = default);
}
