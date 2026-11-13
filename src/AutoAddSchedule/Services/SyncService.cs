using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>本地日程与 iCloud 的同步。由 CalDAV 模块实现。</summary>
public sealed class SyncService : ISyncService
{
    public Task<SyncOutcome> PushAsync(ScheduleItem item, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SyncOutcome.Failure("同步服务尚未实现。"));
    }

    public Task<SyncOutcome> DeleteRemoteAsync(ScheduleItem item, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SyncOutcome.Failure("同步服务尚未实现。"));
    }

    public Task<int> PushPendingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}
