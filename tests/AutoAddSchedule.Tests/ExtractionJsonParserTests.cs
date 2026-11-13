using System;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>日程 JSON 解析的离线测试：正常解析、代码块包裹、字段缺失与时间非法。</summary>
public class ExtractionJsonParserTests
{
    [Fact]
    public void Parse_Should_Read_All_Fields()
    {
        var draft = ScheduleJsonParser.Parse(
            "{\"title\":\"参观校史馆\",\"start\":\"2026-10-28T15:00:00\",\"end\":\"2026-10-28T15:30:00\"," +
            "\"location\":\"校史馆\",\"description\":\"请提前10分钟集合\",\"reminder_minutes_before\":10,\"all_day\":false}");

        Assert.Equal("参观校史馆", draft.Title);
        Assert.Equal(new DateTime(2026, 10, 28, 15, 0, 0), draft.StartLocal);
        Assert.Equal(new DateTime(2026, 10, 28, 15, 30, 0), draft.EndLocal);
        Assert.Equal("校史馆", draft.Location);
        Assert.Equal("请提前10分钟集合", draft.Description);
        Assert.Equal(10, draft.ReminderMinutesBefore);
        Assert.False(draft.AllDay);
    }

    [Fact]
    public void Parse_Should_Accept_Json_Code_Fence()
    {
        var raw = "```json\n" +
                  "{\"title\":\"升旗仪式\",\"start\":\"2026-10-26T06:45:00\"}\n" +
                  "```";

        var draft = ScheduleJsonParser.Parse(raw);

        Assert.Equal("升旗仪式", draft.Title);
        Assert.Equal(new DateTime(2026, 10, 26, 6, 45, 0), draft.StartLocal);
    }

    [Fact]
    public void Parse_Should_Accept_Surrounding_Text()
    {
        var draft = ScheduleJsonParser.Parse(
            "识别结果如下：{\"title\":\"培训会\",\"start\":\"2026-11-24T20:45:00\"}，请核对。");

        Assert.Equal("培训会", draft.Title);
    }

    [Fact]
    public void Parse_Should_Accept_Space_Separated_Time()
    {
        var draft = ScheduleJsonParser.Parse(
            "{\"title\":\"培训会\",\"start\":\"2026-11-24 20:45\"}");

        Assert.Equal(new DateTime(2026, 11, 24, 20, 45, 0), draft.StartLocal);
    }

    [Fact]
    public void Parse_Should_Treat_Date_Only_As_All_Day()
    {
        var draft = ScheduleJsonParser.Parse("{\"title\":\"校运会\",\"start\":\"2026-11-01\"}");

        Assert.True(draft.AllDay);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0), draft.StartLocal);
    }

    [Fact]
    public void Parse_Should_Clamp_Negative_Reminder()
    {
        var draft = ScheduleJsonParser.Parse(
            "{\"title\":\"会议\",\"start\":\"2026-11-24T20:45:00\",\"reminder_minutes_before\":-5}");

        Assert.Equal(0, draft.ReminderMinutesBefore);
    }

    [Fact]
    public void Parse_Should_Unwrap_Nested_Object()
    {
        var draft = ScheduleJsonParser.Parse(
            "{\"schedule\":{\"title\":\"会议\",\"start\":\"2026-11-24T20:45:00\"}}");

        Assert.Equal("会议", draft.Title);
    }

    [Fact]
    public void Parse_Should_Throw_When_Title_Missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleJsonParser.Parse("{\"start\":\"2026-11-24T20:45:00\"}"));

        Assert.Contains("标题", ex.Message);
    }

    [Fact]
    public void Parse_Should_Throw_When_Start_Missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleJsonParser.Parse("{\"title\":\"会议\"}"));

        Assert.Contains("开始时间", ex.Message);
    }

    [Fact]
    public void Parse_Should_Throw_When_Time_Invalid()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleJsonParser.Parse("{\"title\":\"会议\",\"start\":\"明天上午九点\"}"));

        Assert.Contains("无法解析开始时间", ex.Message);
    }

    [Fact]
    public void Parse_Should_Throw_When_End_Before_Start()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScheduleJsonParser.Parse(
                "{\"title\":\"会议\",\"start\":\"2026-11-24T20:45:00\",\"end\":\"2026-11-24T19:00:00\"}"));

        Assert.Contains("结束时间早于开始时间", ex.Message);
    }

    [Fact]
    public void Parse_Should_Throw_When_Response_Empty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScheduleJsonParser.Parse("   "));

        Assert.Contains("内容为空", ex.Message);
    }

    [Fact]
    public void Parse_Should_Throw_When_Response_Has_No_Json()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ScheduleJsonParser.Parse("抱歉，我无法识别这张图片。"));

        Assert.Contains("没有 JSON 对象", ex.Message);
    }
}
