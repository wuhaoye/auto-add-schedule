using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using Xunit.Abstractions;

namespace AutoAddSchedule.Tests;

/// <summary>
/// 真实环境验证（默认跳过）。仅做只读/最小请求：验证 iCloud 账号凭据、日历发现与大模型连接。
/// 运行方式：先设置环境变量，再执行
///   AUTOADD_SCHEDULE_LIVE_CALDAV=1 AUTOADD_SCHEDULE_LIVE_LLM=1 pwsh tools/test.ps1 -Filter FullyQualifiedName~LiveEnvironmentTests
/// 该测试不创建、不修改、不删除任何 iCloud 事件，也不会产生大量大模型费用。
/// </summary>
public class LiveEnvironmentTests
{
    private readonly ITestOutputHelper _output;

    public LiveEnvironmentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Live_CalDav_TestConnection_And_ListCalendars()
    {
        if (Environment.GetEnvironmentVariable("AUTOADD_SCHEDULE_LIVE_CALDAV") != "1")
        {
            _output.WriteLine("未设置 AUTOADD_SCHEDULE_LIVE_CALDAV=1，跳过真实 iCloud 只读验证。");
            return;
        }

        var store = new ConfigStore();
        store.Load();
        var config = store.Config.CalDav;
        Assert.True(config.IsConfigured, "iCloud 配置不完整：需要 Apple ID 与 APPLEKEY（应用专用密码）。");

        var client = new CalDavClient(() => config);
        await client.TestConnectionAsync();
        var calendars = await client.GetCalendarsAsync();

        Assert.NotEmpty(calendars);
        foreach (var calendar in calendars)
        {
            _output.WriteLine("发现日历：" + calendar.DisplayName + " -> " + calendar.Url);
        }
    }

    [Fact]
    public async Task Live_Llm_TestConnection()
    {
        if (Environment.GetEnvironmentVariable("AUTOADD_SCHEDULE_LIVE_LLM") != "1")
        {
            _output.WriteLine("未设置 AUTOADD_SCHEDULE_LIVE_LLM=1，跳过真实大模型连接验证。");
            return;
        }

        var store = new ConfigStore();
        store.Load();
        var config = store.Config.Llm;
        Assert.True(config.IsConfigured, "大模型配置不完整：需要 MODELKEY。");

        var client = new LlmClient(() => config);
        var result = await client.TestConnectionAsync();
        _output.WriteLine("大模型连接测试返回：" + result);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
