using System.Globalization;
using System.Text;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>CalendarBuilder / ICalendarFormat 的离线单元测试（不访问网络）。</summary>
public class CalendarBuilderTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private static CalendarBuilder CreateBuilder() => new(() => FixedUtcNow);

    private static ScheduleItem CreateItem() => new()
    {
        Uid = "event-uid-0001",
        Title = "项目评审",
        StartLocal = new DateTime(2026, 11, 25, 12, 45, 0, DateTimeKind.Local),
        EndLocal = new DateTime(2026, 11, 25, 13, 10, 0, DateTimeKind.Local),
        Location = "会议室A",
        Description = "讨论发布计划",
        ReminderMinutesBefore = 10,
        AllDay = false
    };

    private static List<string> UnfoldLogicalLines(string ics)
    {
        var lines = new List<string>();
        foreach (var line in ics.Split("\r\n", StringSplitOptions.None))
        {
            if (line.Length > 0 && line[0] == ' ' && lines.Count > 0)
            {
                lines[^1] += line[1..];
            }
            else if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static string UnfoldSingleLine(string folded)
    {
        var physical = folded.Split("\r\n", StringSplitOptions.None);
        var builder = new StringBuilder(physical[0]);
        for (var i = 1; i < physical.Length; i++)
        {
            builder.Append(physical[i][1..]);
        }

        return builder.ToString();
    }

    private static void AssertAllPhysicalLinesAtMost75Octets(string text)
    {
        var encoding = new UTF8Encoding(false);
        foreach (var physical in text.Split("\r\n", StringSplitOptions.None))
        {
            if (physical.Length == 0)
            {
                continue;
            }

            var octets = encoding.GetByteCount(physical);
            Assert.True(octets <= ICalendarFormat.MaxLineOctets, "物理行超过 75 字节（" + octets + " 字节）：" + physical);
        }
    }

    private static string LocalToUtcText(DateTime local)
    {
        return DateTime.SpecifyKind(local, DateTimeKind.Local)
            .ToUniversalTime()
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EscapeText_Should_Escape_Backslash_Semicolon_Comma_And_Newline()
    {
        Assert.Equal(@"a\\b\;c\,d\ne", ICalendarFormat.EscapeText("a\\b;c,d\r\ne"));
    }

    [Fact]
    public void EscapeText_Should_Turn_CrLf_Into_Single_Escape_Sequence()
    {
        Assert.Equal(@"第一行\n第二行\n第三行", ICalendarFormat.EscapeText("第一行\r\n第二行\n第三行"));
    }

    [Fact]
    public void EscapeText_Should_Return_Empty_For_Null_Or_Empty()
    {
        Assert.Equal(string.Empty, ICalendarFormat.EscapeText(null));
        Assert.Equal(string.Empty, ICalendarFormat.EscapeText(string.Empty));
    }

    [Fact]
    public void FoldLine_Should_Keep_Short_Line_Unchanged()
    {
        const string line = "SUMMARY:短的日程标题";
        Assert.Equal(line, ICalendarFormat.FoldLine(line));
    }

    [Fact]
    public void FoldLine_Should_Fold_Long_Ascii_Line()
    {
        var line = "DESCRIPTION:" + new string('a', 200);
        var folded = ICalendarFormat.FoldLine(line);
        var physical = folded.Split("\r\n", StringSplitOptions.None);

        Assert.True(physical.Length > 1);
        Assert.All(physical, part => Assert.True(Encoding.UTF8.GetByteCount(part) <= ICalendarFormat.MaxLineOctets));
        Assert.All(physical.Skip(1), part => Assert.StartsWith(" ", part));
        Assert.Equal(line, UnfoldSingleLine(folded));
    }

    [Fact]
    public void FoldLine_Should_Not_Split_Utf8_Multibyte_Characters()
    {
        var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("中文日程", 30));
        var folded = ICalendarFormat.FoldLine(line);
        var physical = folded.Split("\r\n", StringSplitOptions.None);
        var strictUtf8 = new UTF8Encoding(false, true);

        Assert.True(physical.Length > 1);
        Assert.All(physical, part =>
        {
            Assert.True(Encoding.UTF8.GetByteCount(part) <= ICalendarFormat.MaxLineOctets);
            // 严格解码：若把多字节字符拆开，这里会抛异常。
            _ = strictUtf8.GetString(Encoding.UTF8.GetBytes(part));
        });
        Assert.All(physical.Skip(1), part => Assert.StartsWith(" ", part));
        Assert.Equal(line, UnfoldSingleLine(folded));
    }

    [Fact]
    public void BuildEvent_Should_Produce_Complete_Calendar()
    {
        var item = CreateItem();
        var ics = CreateBuilder().BuildEvent(item);

        var expected = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//AutoAddSchedule//CN",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            "UID:event-uid-0001",
            "DTSTAMP:20261113T120000Z",
            "DTSTART:" + LocalToUtcText(item.StartLocal),
            "DTEND:" + LocalToUtcText(item.EndLocal!.Value),
            "SUMMARY:项目评审",
            "LOCATION:会议室A",
            "DESCRIPTION:讨论发布计划",
            "BEGIN:VALARM",
            "ACTION:DISPLAY",
            "TRIGGER:-PT10M",
            "DESCRIPTION:项目评审",
            "END:VALARM",
            "END:VEVENT",
            "END:VCALENDAR"
        };

        Assert.Equal(expected, UnfoldLogicalLines(ics));
        Assert.EndsWith("\r\n", ics);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", string.Empty));
        Assert.DoesNotContain("\r", ics.Replace("\r\n", string.Empty));
        AssertAllPhysicalLinesAtMost75Octets(ics);
    }

    [Fact]
    public void BuildEvent_Should_Default_End_Time_To_Start_Plus_30_Minutes()
    {
        var item = CreateItem();
        item.EndLocal = null;

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        var expectedStart = LocalToUtcText(item.StartLocal);
        var expectedEnd = LocalToUtcText(item.StartLocal.AddMinutes(30));

        Assert.Contains("DTSTART:" + expectedStart, lines);
        Assert.Contains("DTEND:" + expectedEnd, lines);
    }

    [Fact]
    public void BuildEvent_Should_Fix_End_Time_When_It_Is_Not_After_Start()
    {
        var item = CreateItem();
        item.EndLocal = item.StartLocal.AddMinutes(-10);

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        Assert.Contains("DTEND:" + LocalToUtcText(item.StartLocal.AddMinutes(30)), lines);
    }

    [Fact]
    public void BuildEvent_Should_Omit_Valarm_When_Reminder_Is_Zero()
    {
        var item = CreateItem();
        item.ReminderMinutesBefore = 0;

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        Assert.DoesNotContain("BEGIN:VALARM", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("TRIGGER:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildEvent_Should_Omit_Location_And_Description_When_Empty()
    {
        var item = CreateItem();
        item.Location = null;
        item.Description = "   ";
        item.ReminderMinutesBefore = 0;

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        Assert.DoesNotContain(lines, line => line.StartsWith("LOCATION:", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("DESCRIPTION:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildEvent_Should_Use_Date_Values_For_AllDay_Event()
    {
        var item = CreateItem();
        item.AllDay = true;
        item.ReminderMinutesBefore = 0;
        item.EndLocal = null;
        item.StartLocal = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Local);

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        Assert.Contains("DTSTART;VALUE=DATE:20261125", lines);
        Assert.Contains("DTEND;VALUE=DATE:20261126", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("DTSTART:", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("DTEND:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildEvent_Should_Fold_Long_Description()
    {
        var item = CreateItem();
        var description = string.Concat(Enumerable.Repeat("这是一段很长的日程说明", 10));
        item.Description = description;

        var ics = CreateBuilder().BuildEvent(item);
        AssertAllPhysicalLinesAtMost75Octets(ics);
        var lines = UnfoldLogicalLines(ics);
        Assert.Contains("DESCRIPTION:" + description, lines);
    }

    [Fact]
    public void BuildEvent_Should_Escape_Special_Characters_In_Summary()
    {
        var item = CreateItem();
        item.Title = "会议;讨论,第1项\\备注\r\n第二行";
        item.Location = null;
        item.Description = null;
        item.ReminderMinutesBefore = 0;

        var lines = UnfoldLogicalLines(CreateBuilder().BuildEvent(item));
        Assert.Contains(@"SUMMARY:会议\;讨论\,第1项\\备注\n第二行", lines);
    }

    [Fact]
    public void BuildEvent_Should_Throw_When_Item_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() => CreateBuilder().BuildEvent(null!));
    }
}
