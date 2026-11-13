using System.Globalization;
using System.Text;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>按 RFC 5545 生成 iCalendar（VCALENDAR / VEVENT）文本。</summary>
public sealed class CalendarBuilder : ICalendarBuilder
{
    /// <summary>未填写结束时间时的默认日程时长（30 分钟）。</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(30);

    private readonly Func<DateTime> _utcNowProvider;

    /// <summary>生产用构造：DTSTAMP 取当前 UTC 时间。</summary>
    public CalendarBuilder() : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>测试用构造：注入 DTSTAMP 使用的 UTC 时间。</summary>
    public CalendarBuilder(Func<DateTime> utcNowProvider)
    {
        _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
    }

    public string BuildEvent(ScheduleItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var title = string.IsNullOrWhiteSpace(item.Title) ? "未命名日程" : item.Title.Trim();
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//AutoAddSchedule//CN",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            "UID:" + ICalendarFormat.EscapeText(item.Uid),
            "DTSTAMP:" + ICalendarFormat.FormatUtcTimestamp(_utcNowProvider())
        };

        if (item.AllDay)
        {
            // 全天事件用 DATE 值类型，不做时区转换；DTEND 为次日（DTEND 是排他日期）。
            var startDate = item.StartLocal.Date;
            lines.Add("DTSTART;VALUE=DATE:" + startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            lines.Add("DTEND;VALUE=DATE:" + startDate.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }
        else
        {
            var startLocal = item.StartLocal;
            var endLocal = item.EndLocal ?? startLocal.Add(DefaultDuration);
            if (endLocal <= startLocal)
            {
                endLocal = startLocal.Add(DefaultDuration);
            }

            lines.Add("DTSTART:" + ICalendarFormat.FormatUtcFromLocal(startLocal));
            lines.Add("DTEND:" + ICalendarFormat.FormatUtcFromLocal(endLocal));
        }

        lines.Add("SUMMARY:" + ICalendarFormat.EscapeText(title));

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            lines.Add("LOCATION:" + ICalendarFormat.EscapeText(item.Location.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            lines.Add("DESCRIPTION:" + ICalendarFormat.EscapeText(item.Description.Trim()));
        }

        if (item.ReminderMinutesBefore > 0)
        {
            lines.Add("BEGIN:VALARM");
            lines.Add("ACTION:DISPLAY");
            lines.Add("TRIGGER:-PT" + item.ReminderMinutesBefore.ToString(CultureInfo.InvariantCulture) + "M");
            lines.Add("DESCRIPTION:" + ICalendarFormat.EscapeText(title));
            lines.Add("END:VALARM");
        }

        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        return ICalendarFormat.FoldLines(lines);
    }
}

/// <summary>RFC 5545 的文本转义、折行与时间格式化辅助方法（纯函数，便于单元测试）。</summary>
public static class ICalendarFormat
{
    /// <summary>每行最大字节数（UTF-8，RFC 5545 规定 75 个八位组）。</summary>
    public const int MaxLineOctets = 75;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>转义 TEXT 值：反斜杠、分号、逗号与换行（换行转为 \n）。</summary>
    public static string EscapeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case ';':
                    builder.Append("\\;");
                    break;
                case ',':
                    builder.Append("\\,");
                    break;
                case '\r':
                    builder.Append("\\n");
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>本地时间转换为 iCalendar UTC 时间（yyyyMMddTHHmmssZ）。</summary>
    public static string FormatUtcFromLocal(DateTime localTime)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Local);
        return local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>格式化已有 UTC 时间（不再做时区转换）。</summary>
    public static string FormatUtcTimestamp(DateTime utcTime)
    {
        var utc = utcTime.Kind == DateTimeKind.Utc ? utcTime : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 按 75 字节折行，续行以单个空格开头；按字符边界折行，不会把 UTF-8 多字节字符拆开。
    /// 返回值可能包含 CRLF。
    /// </summary>
    public static string FoldLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length == 0 || Utf8.GetByteCount(line) <= MaxLineOctets)
        {
            return line;
        }

        var builder = new StringBuilder(line.Length + (line.Length / 60));
        var current = new StringBuilder();
        var currentBytes = 0;
        var firstLine = true;
        var index = 0;
        while (index < line.Length)
        {
            var unitChars = 1;
            if (char.IsHighSurrogate(line[index]) && index + 1 < line.Length && char.IsLowSurrogate(line[index + 1]))
            {
                unitChars = 2;
            }

            var unit = line.Substring(index, unitChars);
            var unitBytes = Utf8.GetByteCount(unit);
            // 续行第一个字节是空格，因此续行最多只能放 74 个内容字节。
            var limit = firstLine ? MaxLineOctets : MaxLineOctets - 1;
            if (currentBytes + unitBytes > limit)
            {
                builder.Append(current).Append("\r\n ");
                current.Clear();
                currentBytes = 0;
                firstLine = false;
            }

            current.Append(unit);
            currentBytes += unitBytes;
            index += unitChars;
        }

        builder.Append(current);
        return builder.ToString();
    }

    /// <summary>把多行逻辑内容逐行折行，所有物理行以 CRLF 结尾。</summary>
    public static string FoldLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(FoldLine(line)).Append("\r\n");
        }

        return builder.ToString();
    }
}
