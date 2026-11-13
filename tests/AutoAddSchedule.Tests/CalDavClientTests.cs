using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>CalDAV 离线测试共用的配置、XML 样例与响应构造工具。</summary>
internal static class CalDavTestData
{
    public const string ServerUrl = "https://caldav.icloud.com";
    public const string TestAppleId = "tester@example.com";
    public const string TestAppPassword = "abcd-efgh-ijkl-mnop";
    public const string HomeUrl = "https://caldav.icloud.com/123456789/calendars/";
    public const string WorkCalendarUrl = "https://caldav.icloud.com/123456789/calendars/work/";

    public const string SampleIcs =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:event-uid-0001\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    /// <summary>PROPFIND / 返回 current-user-principal 的 207 响应。</summary>
    public const string CurrentUserPrincipalXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <d:multistatus xmlns:d="DAV:">
          <d:response>
            <d:href>/</d:href>
            <d:propstat>
              <d:prop>
                <d:current-user-principal>
                  <d:href>/123456789/principal/</d:href>
                </d:current-user-principal>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
        </d:multistatus>
        """;

    /// <summary>PROPFIND principal / 返回 calendar-home-set 的 207 响应。</summary>
    public const string CalendarHomeSetXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
          <d:response>
            <d:href>/123456789/principal/</d:href>
            <d:propstat>
              <d:prop>
                <c:calendar-home-set>
                  <d:href>/123456789/calendars/</d:href>
                </c:calendar-home-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
        </d:multistatus>
        """;

    /// <summary>PROPFIND calendar-home Depth:1：包含 2 个日历、1 个只支持 VTODO 的日历、1 个无显示名日历。</summary>
    public const string CalendarsXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
          <d:response>
            <d:href>/123456789/calendars/</d:href>
            <d:propstat>
              <d:prop>
                <d:displayname>日历</d:displayname>
                <d:resourcetype><d:collection/></d:resourcetype>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/123456789/calendars/work/</d:href>
            <d:propstat>
              <d:prop>
                <d:displayname>工作</d:displayname>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                <c:supported-calendar-component-set><c:comp name="VEVENT"/></c:supported-calendar-component-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/123456789/calendars/home/</d:href>
            <d:propstat>
              <d:prop>
                <d:displayname>家庭</d:displayname>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
            <d:propstat>
              <d:prop><d:getetag/></d:prop>
              <d:status>HTTP/1.1 404 Not Found</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/123456789/calendars/reminders/</d:href>
            <d:propstat>
              <d:prop>
                <d:displayname>提醒事项</d:displayname>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
                <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/123456789/calendars/reading/</d:href>
            <d:propstat>
              <d:prop>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
        </d:multistatus>
        """;

    public static CalDavConfig CreateConfig() => new()
    {
        ServerUrl = ServerUrl,
        AppleId = TestAppleId,
        AppPassword = TestAppPassword,
        CalendarHref = WorkCalendarUrl,
        CalendarDisplayName = "工作"
    };

    public static CalDavConfig CreateConfigWithoutCredentials() => new()
    {
        ServerUrl = ServerUrl
    };

    public static HttpResponseMessage MultiStatusResponse(string xml) => new(HttpStatusCode.MultiStatus)
    {
        Content = new StringContent(xml, Encoding.UTF8, "application/xml")
    };

    public static HttpResponseMessage StatusResponse(HttpStatusCode statusCode, string? etag = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }
}

/// <summary>CalDavClient 的离线单元测试：使用假 HttpMessageHandler，不访问真实 iCloud，也不产生真实写入。</summary>
public class CalDavClientTests
{
    private static CalDavClient CreateClient(StubHandler handler, CalDavConfig? config = null)
    {
        var effectiveConfig = config ?? CalDavTestData.CreateConfig();
        return new CalDavClient(() => effectiveConfig, new HttpClient(handler));
    }

    private static CalendarInfo CreateWorkCalendar() => new()
    {
        Href = "/123456789/calendars/work/",
        Url = CalDavTestData.WorkCalendarUrl,
        DisplayName = "工作"
    };

    [Fact]
    public async Task TestConnectionAsync_Should_Send_Propfind_With_Basic_Auth()
    {
        var handler = new StubHandler(_ => CalDavTestData.MultiStatusResponse(CalDavTestData.CurrentUserPrincipalXml));
        var client = CreateClient(handler);

        await client.TestConnectionAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("PROPFIND", request.Method.Method);
        Assert.Equal(CalDavTestData.ServerUrl + "/", request.RequestUri!.AbsoluteUri);
        Assert.Equal("0", Assert.Single(request.Headers.GetValues("Depth")));
        Assert.Contains("current-user-principal", handler.Bodies[0]);

        var authorization = Assert.IsType<AuthenticationHeaderValue>(request.Headers.Authorization);
        Assert.Equal("Basic", authorization.Scheme);
        var expectedToken = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(CalDavTestData.TestAppleId + ":" + CalDavTestData.TestAppPassword));
        Assert.Equal(expectedToken, authorization.Parameter);
    }

    [Theory]
    [InlineData(401, "应用专用密码")]
    [InlineData(403, "拒绝访问")]
    [InlineData(404, "找不到 CalDAV 资源")]
    [InlineData(500, "暂时不可用")]
    public async Task TestConnectionAsync_Should_Map_Http_Status_To_Chinese_Message(int statusCode, string expectedFragment)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)statusCode));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CalDavException>(() => client.TestConnectionAsync());

        Assert.Contains(expectedFragment, exception.Message);
        Assert.Equal((HttpStatusCode)statusCode, exception.StatusCode);
    }

    [Fact]
    public async Task TestConnectionAsync_Should_Map_Network_Exception_To_Chinese_Message()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("模拟网络故障"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CalDavException>(() => client.TestConnectionAsync());

        Assert.Contains("无法连接 iCloud", exception.Message);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task GetCalendarsAsync_Should_Discover_Calendars()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://caldav.icloud.com/" => CalDavTestData.MultiStatusResponse(CalDavTestData.CurrentUserPrincipalXml),
            "https://caldav.icloud.com/123456789/principal/" => CalDavTestData.MultiStatusResponse(CalDavTestData.CalendarHomeSetXml),
            "https://caldav.icloud.com/123456789/calendars/" => CalDavTestData.MultiStatusResponse(CalDavTestData.CalendarsXml),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var calendars = await client.GetCalendarsAsync();

        Assert.Equal(3, calendars.Count);
        Assert.Equal("工作", calendars[0].DisplayName);
        Assert.Equal(CalDavTestData.WorkCalendarUrl, calendars[0].Url);
        Assert.Equal("家庭", calendars[1].DisplayName);
        Assert.All(calendars, calendar => Assert.StartsWith("https://caldav.icloud.com/", calendar.Url));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("PROPFIND", request.Method.Method));
        Assert.Equal(
            new[] { "0", "0", "1" },
            handler.Requests.Select(request => request.Headers.GetValues("Depth").Single()));
    }

    [Fact]
    public async Task GetCalendarsAsync_Should_Throw_Chinese_Message_When_Not_Configured()
    {
        var handler = new StubHandler(_ => CalDavTestData.MultiStatusResponse(CalDavTestData.CurrentUserPrincipalXml));
        var client = CreateClient(handler, CalDavTestData.CreateConfigWithoutCredentials());

        var exception = await Assert.ThrowsAsync<CalDavException>(() => client.GetCalendarsAsync());

        Assert.Contains("尚未配置 iCloud 账号", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateEventAsync_Should_Put_Event_With_If_None_Match()
    {
        var handler = new StubHandler(_ => CalDavTestData.StatusResponse(HttpStatusCode.Created, "\"etag-1\""));
        var client = CreateClient(handler);

        var result = await client.CreateEventAsync(CreateWorkCalendar(), CalDavTestData.SampleIcs, "event-uid-0001");

        var expectedUrl = CalDavTestData.WorkCalendarUrl + "event-uid-0001.ics";
        Assert.Equal(expectedUrl, result.Href);
        Assert.Equal("\"etag-1\"", result.ETag);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(expectedUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("*", Assert.Single(request.Headers.GetValues("If-None-Match")));
        Assert.Equal("text/calendar", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", request.Content.Headers.ContentType.CharSet);
        Assert.Equal(CalDavTestData.SampleIcs, handler.Bodies[0]);
    }

    [Fact]
    public async Task CreateEventAsync_Should_Handle_Calendar_Url_Without_Trailing_Slash()
    {
        var handler = new StubHandler(_ => CalDavTestData.StatusResponse(HttpStatusCode.Created));
        var client = CreateClient(handler);
        var calendar = new CalendarInfo
        {
            Href = "/123456789/calendars/work",
            Url = "https://caldav.icloud.com/123456789/calendars/work",
            DisplayName = "工作"
        };

        await client.CreateEventAsync(calendar, CalDavTestData.SampleIcs, "event-uid-0001");

        Assert.Equal(
            "https://caldav.icloud.com/123456789/calendars/work/event-uid-0001.ics",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CreateEventAsync_Should_Map_Server_Error_To_Chinese()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CalDavException>(
            () => client.CreateEventAsync(CreateWorkCalendar(), CalDavTestData.SampleIcs, "event-uid-0001"));

        Assert.Contains("暂时不可用", exception.Message);
    }

    [Fact]
    public async Task UpdateEventAsync_Should_Put_With_If_Match()
    {
        var handler = new StubHandler(_ => CalDavTestData.StatusResponse(HttpStatusCode.NoContent));
        var client = CreateClient(handler);
        var eventUrl = CalDavTestData.WorkCalendarUrl + "event-uid-0001.ics";

        var result = await client.UpdateEventAsync(CreateWorkCalendar(), eventUrl, CalDavTestData.SampleIcs, "\"etag-2\"");

        Assert.Equal("\"etag-2\"", result.ETag);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(eventUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("\"etag-2\"", Assert.Single(request.Headers.GetValues("If-Match")));
        Assert.False(request.Headers.Contains("If-None-Match"));
    }

    [Fact]
    public async Task UpdateEventAsync_Should_Use_Star_When_ETag_Missing()
    {
        var handler = new StubHandler(_ => CalDavTestData.StatusResponse(HttpStatusCode.NoContent));
        var client = CreateClient(handler);
        var eventUrl = CalDavTestData.WorkCalendarUrl + "event-uid-0001.ics";

        await client.UpdateEventAsync(CreateWorkCalendar(), eventUrl, CalDavTestData.SampleIcs, null);

        Assert.Equal("*", Assert.Single(Assert.Single(handler.Requests).Headers.GetValues("If-Match")));
    }

    [Fact]
    public async Task DeleteEventAsync_Should_Send_Delete_With_If_Match()
    {
        var handler = new StubHandler(_ => CalDavTestData.StatusResponse(HttpStatusCode.NoContent));
        var client = CreateClient(handler);
        var eventUrl = CalDavTestData.WorkCalendarUrl + "event-uid-0001.ics";

        await client.DeleteEventAsync(CreateWorkCalendar(), eventUrl, "\"etag-3\"");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(eventUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("\"etag-3\"", Assert.Single(request.Headers.GetValues("If-Match")));
    }

    [Fact]
    public async Task DeleteEventAsync_Should_Treat_404_As_Success()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        await client.DeleteEventAsync(
            CreateWorkCalendar(),
            CalDavTestData.WorkCalendarUrl + "event-uid-0001.ics",
            null);
    }

    [Fact]
    public async Task GetCalendarsAsync_Should_Throw_Chinese_Message_On_Unparsable_Xml()
    {
        var handler = new StubHandler(_ => CalDavTestData.MultiStatusResponse("这不是合法的 XML 响应"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<CalDavException>(() => client.GetCalendarsAsync());

        Assert.Contains("无法解析", exception.Message);
    }

    /// <summary>记录请求并返回预设响应的假 HttpMessageHandler。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            return _responder(request);
        }
    }
}

/// <summary>CalDAV XML 解析的离线单元测试。</summary>
public class CalDavXmlParserTests
{
    [Fact]
    public void ParseMultiStatus_Should_Return_All_Responses()
    {
        var responses = CalDavXmlParser.ParseMultiStatus(CalDavTestData.CalendarsXml);

        Assert.Equal(5, responses.Count);

        Assert.Equal("/123456789/calendars/", responses[0].Href);
        Assert.False(responses[0].IsCalendar);

        Assert.Equal("/123456789/calendars/work/", responses[1].Href);
        Assert.True(responses[1].IsCalendar);
        Assert.True(responses[1].SupportsEvents);
        Assert.Equal("工作", responses[1].DisplayName);

        // 404 的 propstat 被忽略，200 的 displayname 仍然生效。
        Assert.Equal("家庭", responses[2].DisplayName);

        // 只支持 VTODO 的提醒事项日历不支持 VEVENT。
        Assert.False(responses[3].SupportsEvents);

        // 未返回 supported-calendar-component-set 时无法判断，不做排除。
        Assert.Null(responses[4].SupportsEvents);
    }

    [Fact]
    public void ParseCalendars_Should_Parse_Url_And_DisplayName()
    {
        var calendars = CalDavXmlParser.ParseCalendars(CalDavTestData.CalendarsXml, CalDavTestData.HomeUrl);

        Assert.Equal(3, calendars.Count);
        Assert.Equal("工作", calendars[0].DisplayName);
        Assert.Equal("/123456789/calendars/work/", calendars[0].Href);
        Assert.Equal(CalDavTestData.WorkCalendarUrl, calendars[0].Url);

        Assert.Equal("家庭", calendars[1].DisplayName);
        Assert.Equal("https://caldav.icloud.com/123456789/calendars/home/", calendars[1].Url);

        // 没有 displayname 时回退为 href 的最后一段。
        Assert.Equal("reading", calendars[2].DisplayName);
        Assert.Equal("https://caldav.icloud.com/123456789/calendars/reading/", calendars[2].Url);
    }

    [Fact]
    public void ParseCalendars_Should_Skip_Non_Calendar_And_Todo_Only_Collections()
    {
        var calendars = CalDavXmlParser.ParseCalendars(CalDavTestData.CalendarsXml, CalDavTestData.HomeUrl);

        Assert.DoesNotContain(calendars, calendar => calendar.DisplayName == "日历");
        Assert.DoesNotContain(calendars, calendar => calendar.DisplayName == "提醒事项");
    }

    [Fact]
    public void ParseCurrentUserPrincipalHref_Should_Return_Href()
    {
        var href = CalDavXmlParser.ParseCurrentUserPrincipalHref(CalDavTestData.CurrentUserPrincipalXml);

        Assert.Equal("/123456789/principal/", href);
    }

    [Fact]
    public void ParseCalendarHomeSetHref_Should_Return_Href()
    {
        var href = CalDavXmlParser.ParseCalendarHomeSetHref(CalDavTestData.CalendarHomeSetXml);

        Assert.Equal("/123456789/calendars/", href);
    }

    [Fact]
    public void ParseCurrentUserPrincipalHref_Should_Return_Null_When_Property_Missing()
    {
        Assert.Null(CalDavXmlParser.ParseCurrentUserPrincipalHref(CalDavTestData.CalendarsXml));
    }

    [Fact]
    public void ParseMultiStatus_Should_Throw_FormatException_On_Invalid_Xml()
    {
        Assert.Throws<FormatException>(() => CalDavXmlParser.ParseMultiStatus("<html>错误页</html>"));
        Assert.Throws<FormatException>(() => CalDavXmlParser.ParseMultiStatus(""));
    }

    [Theory]
    [InlineData("https://caldav.icloud.com", "/123456789/calendars/work/", "https://caldav.icloud.com/123456789/calendars/work/")]
    [InlineData("https://caldav.icloud.com/123456789/calendars", "work/", "https://caldav.icloud.com/123456789/calendars/work/")]
    [InlineData("https://caldav.icloud.com/", "https://p01-caldav.icloud.com/1/principal/", "https://p01-caldav.icloud.com/1/principal/")]
    public void ResolveUrl_Should_Resolve_Relative_And_Absolute_Href(string baseUrl, string href, string expected)
    {
        Assert.Equal(expected, CalDavXmlParser.ResolveUrl(baseUrl, href));
    }

    [Fact]
    public void ResolveUrl_Should_Throw_On_Empty_Href()
    {
        Assert.Throws<FormatException>(() => CalDavXmlParser.ResolveUrl(CalDavTestData.ServerUrl, " "));
    }
}
