namespace AutoAddSchedule.Services;

/// <summary>iCloud 日历中的一个日历集合。</summary>
public sealed class CalendarInfo
{
    /// <summary>CalDAV href（服务器返回的相对路径）。</summary>
    public string Href { get; init; } = string.Empty;

    /// <summary>绝对 URL。</summary>
    public string Url { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public override string ToString() => DisplayName;
}

/// <summary>CalDAV 写入结果。</summary>
public sealed class CalDavWriteResult
{
    public string Href { get; init; } = string.Empty;

    public string? ETag { get; init; }
}

public interface ICalDavClient
{
    /// <summary>验证账号与应用专用密码，失败抛异常（中文消息）。</summary>
    Task TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>发现当前账号下的全部日历。</summary>
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default);

    /// <summary>在指定日历中创建事件。</summary>
    Task<CalDavWriteResult> CreateEventAsync(
        CalendarInfo calendar,
        string icsContent,
        string uid,
        CancellationToken cancellationToken = default);

    /// <summary>更新已有事件（If-Match 使用 ETag）。</summary>
    Task<CalDavWriteResult> UpdateEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string icsContent,
        string? etag,
        CancellationToken cancellationToken = default);

    /// <summary>删除已有事件。</summary>
    Task DeleteEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string? etag,
        CancellationToken cancellationToken = default);
}
