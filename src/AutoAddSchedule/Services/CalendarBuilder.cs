using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>按 RFC 5545 生成 iCalendar 内容。由 CalDAV 模块实现。</summary>
public sealed class CalendarBuilder : ICalendarBuilder
{
    public string BuildEvent(ScheduleItem item)
    {
        throw new NotImplementedException("iCalendar 生成器尚未实现。");
    }
}
