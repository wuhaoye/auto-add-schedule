namespace AutoAddSchedule.Tests;

/// <summary>基础冒烟测试：确认测试项目能引用主程序并运行。</summary>
public class SmokeTests
{
    [Fact]
    public void TimeFormat_Should_Format_Reminder_Time()
    {
        var value = AutoAddSchedule.Core.TimeFormat.FormatReminderTime(new DateTime(2026, 10, 26, 6, 45, 0));
        Assert.Equal("2026年10月26日6：45", value);
    }
}
