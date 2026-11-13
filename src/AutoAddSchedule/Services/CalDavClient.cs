namespace AutoAddSchedule.Services;

/// <summary>iCloud CalDAV 客户端。由 CalDAV 模块实现。</summary>
public sealed class CalDavClient : ICalDavClient
{
    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("iCloud CalDAV 客户端尚未实现。");
    }

    public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("iCloud CalDAV 客户端尚未实现。");
    }

    public Task<CalDavWriteResult> CreateEventAsync(
        CalendarInfo calendar,
        string icsContent,
        string uid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("iCloud CalDAV 客户端尚未实现。");
    }

    public Task<CalDavWriteResult> UpdateEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string icsContent,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("iCloud CalDAV 客户端尚未实现。");
    }

    public Task DeleteEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("iCloud CalDAV 客户端尚未实现。");
    }
}
