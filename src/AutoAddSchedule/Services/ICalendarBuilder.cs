using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

public interface ICalendarBuilder
{
    /// <summary>按 RFC 5545 生成单个事件的完整 VCALENDAR 文本（含 VALARM 提醒）。</summary>
    string BuildEvent(ScheduleItem item);
}
