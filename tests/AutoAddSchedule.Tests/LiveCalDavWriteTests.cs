using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using Xunit.Abstractions;

namespace AutoAddSchedule.Tests;

/// <summary>
/// 真实 iCloud 写入验证（默认跳过，会创建并删除一条测试事件）。
/// 运行方式：
///   $env:AUTOADD_SCHEDULE_LIVE_CALDAV_WRITE='1'
///   $env:AUTOADD_SCHEDULE_LIVE_CALDAV_URL='https://p218-caldav.icloud.com.cn/xxx/calendars/yyy/'
///   pwsh tools/test.ps1 -Filter "FullyQualifiedName~LiveCalDavWriteTests"
/// 凭据来自仓库根 .env（APPLEID 为手机号时用 86+手机号 的写法）。
/// </summary>
public class LiveCalDavWriteTests
{
    private readonly ITestOutputHelper _output;

    public LiveCalDavWriteTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Live_Create_Verify_Delete_Event()
    {
        if (Environment.GetEnvironmentVariable("AUTOADD_SCHEDULE_LIVE_CALDAV_WRITE") != "1")
        {
            _output.WriteLine("未设置 AUTOADD_SCHEDULE_LIVE_CALDAV_WRITE=1，跳过真实 iCloud 写入验证。");
            return;
        }

        var calendarUrl = Environment.GetEnvironmentVariable("AUTOADD_SCHEDULE_LIVE_CALDAV_URL");
        Assert.False(string.IsNullOrWhiteSpace(calendarUrl), "需要设置 AUTOADD_SCHEDULE_LIVE_CALDAV_URL（目标日历绝对 URL）");

        var store = new ConfigStore();
        store.Load();
        var config = store.Config.CalDav;
        Assert.True(config.IsConfigured, "iCloud 配置不完整：需要 Apple ID 与 APPLEKEY（应用专用密码）。");

        var calendar = new CalendarInfo
        {
            Href = calendarUrl!,
            Url = calendarUrl!,
            DisplayName = "同步验证"
        };

        var client = new CalDavClient(() => config);
        var builder = new CalendarBuilder();

        var start = DateTime.Now.AddMinutes(10);
        var item = new ScheduleItem
        {
            Title = "AutoAddSchedule 同步测试",
            Description = "由 AutoAddSchedule 集成测试创建，验证后自动删除。",
            StartLocal = start,
            EndLocal = start.AddMinutes(5),
            ReminderMinutesBefore = 5,
            Source = ScheduleSource.Manual
        };

        var ics = builder.BuildEvent(item);
        var created = await client.CreateEventAsync(calendar, ics, item.Uid);
        _output.WriteLine("创建成功：Href=" + created.Href + " ETag=" + created.ETag);
        Assert.False(string.IsNullOrWhiteSpace(created.Href));

        var eventUrl = ResolveEventUrl(calendar.Url, created.Href, item.Uid);
        using var http = CreateHttpClient(config);

        var before = await PropFindAsync(http, eventUrl);
        _output.WriteLine("创建后 PROPFIND：HTTP " + before);
        Assert.InRange(before, 200, 299);

        await client.DeleteEventAsync(calendar, eventUrl, created.ETag);
        _output.WriteLine("测试事件已删除。");

        var after = await PropFindAsync(http, eventUrl);
        _output.WriteLine("删除后 PROPFIND：HTTP " + after);
        Assert.Equal(404, after);
    }

    private static string ResolveEventUrl(string calendarUrl, string createdHref, string uid)
    {
        if (createdHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return createdHref;
        }

        return calendarUrl.TrimEnd('/') + "/" + uid + ".ics";
    }

    private static HttpClient CreateHttpClient(CalDavConfig config)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(config.AppleId + ":" + config.AppPassword));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return client;
    }

    private static async Task<int> PropFindAsync(HttpClient http, string url)
    {
        const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getetag/></d:prop></d:propfind>";
        using var content = new StringContent(body, Encoding.UTF8, "application/xml");
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url) { Content = content };
        request.Headers.TryAddWithoutValidation("Depth", "0");
        using var response = await http.SendAsync(request);
        return (int)response.StatusCode;
    }
}