using System;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>提示词离线测试：日期注入、字段约束与补充信息规则。</summary>
public class LlmPromptTests
{
    [Fact]
    public void BuildSystemPrompt_Should_Inject_Current_Date_And_Year()
    {
        var prompt = LlmPrompt.BuildSystemPrompt(new DateTime(2026, 11, 13, 20, 24, 44));

        Assert.Contains("2026-11-13 20:24:44", prompt);
        Assert.Contains("2026", prompt);
        Assert.Contains("星期五", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Should_Contain_Json_Fields_And_Time_Rules()
    {
        var prompt = LlmPrompt.BuildSystemPrompt(new DateTime(2026, 11, 13, 20, 24, 44));

        Assert.Contains("\"title\"", prompt);
        Assert.Contains("\"start\"", prompt);
        Assert.Contains("\"end\"", prompt);
        Assert.Contains("\"location\"", prompt);
        Assert.Contains("\"description\"", prompt);
        Assert.Contains("\"reminder_minutes_before\"", prompt);
        Assert.Contains("\"all_day\"", prompt);
        Assert.Contains("集合时间", prompt);
        Assert.Contains("提前", prompt);
        Assert.Contains("补充信息", prompt);
        Assert.Contains("覆盖", prompt);
        Assert.Contains("当前年份", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Should_Use_Injected_Year()
    {
        var prompt = LlmPrompt.BuildSystemPrompt(new DateTime(2030, 1, 2, 8, 0, 0));

        Assert.Contains("2030-01-02 08:00:00", prompt);
        Assert.Contains("星期三", prompt);
        Assert.Contains("2030", prompt);
    }

    [Fact]
    public void BuildUserPrompt_Should_Include_Supplement()
    {
        var prompt = LlmPrompt.BuildUserPrompt("提前30分钟提醒我");

        Assert.Contains("补充信息", prompt);
        Assert.Contains("提前30分钟提醒我", prompt);
        Assert.Contains("覆盖", prompt);
    }

    [Fact]
    public void BuildUserPrompt_Should_Handle_Empty_Supplement()
    {
        var prompt = LlmPrompt.BuildUserPrompt(null);

        Assert.Contains("补充信息", prompt);
        Assert.Contains("无", prompt);
    }
}
