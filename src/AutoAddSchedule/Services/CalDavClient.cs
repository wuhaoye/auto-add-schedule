using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>CalDAV 操作失败。Message 为可直接展示给用户的中文提示。</summary>
public sealed class CalDavException : Exception
{
    public CalDavException(string message) : base(message)
    {
    }

    public CalDavException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>HTTP 状态码；网络异常或本地校验失败时为 null。</summary>
    public HttpStatusCode? StatusCode { get; init; }
}

/// <summary>
/// iCloud CalDAV 客户端：HTTP Basic 认证、日历发现、事件创建 / 更新 / 删除。
/// 默认使用 <see cref="AppServices.Config"/> 中的配置，也可通过构造函数注入配置与 HttpClient 以便离线测试。
/// </summary>
public sealed class CalDavClient : ICalDavClient
{
    private const int MaxRedirects = 3;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const string PropFindPrincipalBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:">
          <d:prop>
            <d:current-user-principal/>
          </d:prop>
        </d:propfind>
        """;

    private const string PropFindHomeSetBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
          <d:prop>
            <c:calendar-home-set/>
          </d:prop>
        </d:propfind>
        """;

    private const string PropFindCalendarsBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
          <d:prop>
            <d:displayname/>
            <d:resourcetype/>
            <c:supported-calendar-component-set/>
          </d:prop>
        </d:propfind>
        """;

    private readonly Func<CalDavConfig> _configProvider;
    private readonly HttpClient _httpClient;

    /// <summary>生产用无参构造，供 AppServices 使用。</summary>
    public CalDavClient()
        : this(() => AppServices.Config.Config.CalDav, null)
    {
    }

    /// <summary>
    /// 测试用构造。
    /// </summary>
    /// <param name="configProvider">返回当前 CalDAV 配置的委托，每次请求时调用。</param>
    /// <param name="httpClient">可选的 HttpClient（注入后由调用方负责生命周期）。</param>
    public CalDavClient(Func<CalDavConfig> configProvider, HttpClient? httpClient = null)
    {
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        if (httpClient is not null)
        {
            _httpClient = httpClient;
        }
        else
        {
            // 手动跟随重定向：HttpClient 自动重定向时会丢弃 Authorization，iCloud 可能先跳到分区主机。
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All
            };
            _httpClient = new HttpClient(handler) { Timeout = DefaultTimeout };
        }
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = GetConfig();
        using var response = await PropFindAsync(
            NormalizeServerUrl(config.ServerUrl),
            PropFindPrincipalBody,
            "0",
            config,
            "验证 iCloud 账号",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "验证 iCloud 账号");
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        var config = GetConfig();
        var serverUrl = NormalizeServerUrl(config.ServerUrl);

        try
        {
            string principalXml;
            using (var rootResponse = await PropFindAsync(
                       serverUrl, PropFindPrincipalBody, "0", config, "发现用户主体", cancellationToken)
                       .ConfigureAwait(false))
            {
                EnsureSuccess(rootResponse, "发现用户主体");
                principalXml = await rootResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var principalHref = CalDavXmlParser.ParseCurrentUserPrincipalHref(principalXml)
                                 ?? throw new CalDavException("iCloud 未返回 current-user-principal，无法发现日历。");
            var principalUrl = CalDavXmlParser.ResolveUrl(serverUrl, principalHref);

            string homeSetXml;
            using (var principalResponse = await PropFindAsync(
                       principalUrl, PropFindHomeSetBody, "0", config, "发现日历主目录", cancellationToken)
                       .ConfigureAwait(false))
            {
                EnsureSuccess(principalResponse, "发现日历主目录");
                homeSetXml = await principalResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var homeHref = CalDavXmlParser.ParseCalendarHomeSetHref(homeSetXml)
                           ?? throw new CalDavException("iCloud 未返回 calendar-home-set，无法发现日历。");
            var homeUrl = CalDavXmlParser.ResolveUrl(principalUrl, homeHref);

            string calendarsXml;
            using (var homeResponse = await PropFindAsync(
                       homeUrl, PropFindCalendarsBody, "1", config, "读取日历列表", cancellationToken)
                       .ConfigureAwait(false))
            {
                EnsureSuccess(homeResponse, "读取日历列表");
                calendarsXml = await homeResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var calendars = CalDavXmlParser.ParseCalendars(calendarsXml, homeUrl);
            if (calendars.Count == 0)
            {
                throw new CalDavException("没有找到可用的 iCloud 日历，请先在 iCloud 日历中创建日历并确认账号状态。");
            }

            AppLog.Info("iCloud 日历发现完成，共 " + calendars.Count + " 个。");
            return calendars;
        }
        catch (FormatException ex)
        {
            throw new CalDavException("iCloud 返回的 CalDAV 数据无法解析，请稍后重试或检查服务器地址。", ex);
        }
    }

    public async Task<CalDavWriteResult> CreateEventAsync(
        CalendarInfo calendar,
        string icsContent,
        string uid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (string.IsNullOrWhiteSpace(icsContent))
        {
            throw new ArgumentException("iCalendar 内容不能为空。", nameof(icsContent));
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            throw new ArgumentException("日程 UID 不能为空。", nameof(uid));
        }

        var config = GetConfig();
        var eventUrl = BuildEventUrl(calendar, uid);

        using var response = await SendWithRedirectsAsync(
            url => CreatePutRequest(url, icsContent, ifMatch: null, ifNoneMatch: "*"),
            eventUrl,
            config,
            "创建日程",
            cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response, "创建日程");
        var href = response.RequestMessage?.RequestUri?.ToString() ?? eventUrl;
        AppLog.Info("已在 iCloud 创建日程事件：" + href);
        return new CalDavWriteResult
        {
            Href = href,
            ETag = response.Headers.ETag?.ToString()
        };
    }

    public async Task<CalDavWriteResult> UpdateEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string icsContent,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (string.IsNullOrWhiteSpace(icsContent))
        {
            throw new ArgumentException("iCalendar 内容不能为空。", nameof(icsContent));
        }

        if (string.IsNullOrWhiteSpace(eventUrl))
        {
            throw new ArgumentException("事件地址不能为空。", nameof(eventUrl));
        }

        var config = GetConfig();
        var url = ResolveEventUrl(calendar, eventUrl);
        var ifMatch = string.IsNullOrWhiteSpace(etag) ? "*" : etag.Trim();

        using var response = await SendWithRedirectsAsync(
            target => CreatePutRequest(target, icsContent, ifMatch, ifNoneMatch: null),
            url,
            config,
            "更新日程",
            cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response, "更新日程");
        var href = response.RequestMessage?.RequestUri?.ToString() ?? url;
        AppLog.Info("已更新 iCloud 日程事件：" + href);
        return new CalDavWriteResult
        {
            Href = href,
            ETag = response.Headers.ETag?.ToString() ?? etag
        };
    }

    public async Task DeleteEventAsync(
        CalendarInfo calendar,
        string eventUrl,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (string.IsNullOrWhiteSpace(eventUrl))
        {
            throw new ArgumentException("事件地址不能为空。", nameof(eventUrl));
        }

        var config = GetConfig();
        var url = ResolveEventUrl(calendar, eventUrl);

        using var response = await SendWithRedirectsAsync(
            target =>
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, target);
                if (!string.IsNullOrWhiteSpace(etag))
                {
                    request.Headers.TryAddWithoutValidation("If-Match", etag.Trim());
                }

                return request;
            },
            url,
            config,
            "删除日程",
            cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 404)
        {
            // 远端事件已不存在（可能已在其他设备删除），按删除成功处理，避免本地删除项永远无法清理。
            AppLog.Warn("iCloud 上已不存在该事件，按删除成功处理：" + url);
            return;
        }

        EnsureSuccess(response, "删除日程");
        AppLog.Info("已删除 iCloud 日程事件：" + url);
    }

    private Task<HttpResponseMessage> PropFindAsync(
        string url,
        string body,
        string depth,
        CalDavConfig config,
        string operation,
        CancellationToken cancellationToken)
    {
        return SendWithRedirectsAsync(
            target =>
            {
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), target)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/xml")
                };
                request.Headers.TryAddWithoutValidation("Depth", depth);
                return request;
            },
            url,
            config,
            operation,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Func<string, HttpRequestMessage> requestFactory,
        string url,
        CalDavConfig config,
        string operation,
        CancellationToken cancellationToken)
    {
        var currentUrl = url;
        for (var attempt = 0; ; attempt++)
        {
            var request = requestFactory(currentUrl);
            ApplyAuthorization(request, config);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CalDavException("连接 iCloud 超时，请检查网络后重试。", ex);
            }
            catch (HttpRequestException ex)
            {
                AppLog.Error("CalDAV " + operation + " 网络请求失败", ex);
                throw new CalDavException("无法连接 iCloud 服务，请检查网络连接或代理设置。", ex);
            }

            response.RequestMessage ??= request;

            if (!IsRedirect(response.StatusCode) || attempt >= MaxRedirects)
            {
                return response;
            }

            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrWhiteSpace(location))
            {
                return response;
            }

            string nextUrl;
            try
            {
                nextUrl = CalDavXmlParser.ResolveUrl(currentUrl, location);
            }
            catch (FormatException)
            {
                return response;
            }

            AppLog.Info("CalDAV " + operation + " 跟随重定向：" + currentUrl + " -> " + nextUrl);
            currentUrl = nextUrl;
            response.Dispose();
        }
    }

    private static HttpRequestMessage CreatePutRequest(string url, string icsContent, string? ifMatch, string? ifNoneMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(icsContent, Encoding.UTF8, "text/calendar")
        };
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return request;
    }

    private static void ApplyAuthorization(HttpRequestMessage request, CalDavConfig config)
    {
        // 只在这里拼接认证信息；任何日志与异常都不得包含 Authorization 或密码。
        var credential = config.AppleId.Trim() + ":" + config.AppPassword.Trim();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        AppLog.Warn("CalDAV " + operation + " 失败：HTTP " + status);
        throw MapHttpError(status, operation);
    }

    private static CalDavException MapHttpError(int status, string operation)
    {
        var message = status switch
        {
            401 => "认证失败：Apple ID 或应用专用密码不正确。请确认 Apple ID 邮箱，并使用 iCloud 应用专用密码（不是 Apple ID 登录密码）。",
            403 => "iCloud 拒绝访问（403）：请确认 Apple ID 已开启双重认证，并重新生成应用专用密码。",
            404 => "找不到 CalDAV 资源（404）：服务器地址或日历地址可能已失效，请重新读取日历列表。",
            409 => "iCloud 返回冲突（409）：该日程可能已被其他设备修改，请刷新后重试。",
            412 => "iCloud 返回前置条件失败（412）：该日程已在其他设备上被修改，请刷新后重试。",
            429 => "请求过于频繁（429）：iCloud 暂时限制访问，请稍后重试。",
            >= 500 and <= 599 => "iCloud 服务暂时不可用（HTTP " + status + "），请稍后重试。",
            _ => "iCloud 返回错误（HTTP " + status + "）：" + operation + "失败。"
        };
        return new CalDavException(message) { StatusCode = (HttpStatusCode)status };
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return (int)statusCode is 301 or 302 or 303 or 307 or 308;
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        var trimmed = serverUrl.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
    }

    private static string BuildEventUrl(CalendarInfo calendar, string uid)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(calendar.Url) ? calendar.Url.Trim() : calendar.Href.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new CalDavException("未指定目标日历地址，无法创建日程。");
        }

        return baseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(uid) + ".ics";
    }

    private static string ResolveEventUrl(CalendarInfo calendar, string eventUrl)
    {
        if (Uri.TryCreate(eventUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var baseUrl = !string.IsNullOrWhiteSpace(calendar.Url) ? calendar.Url : calendar.Href;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new CalDavException("未指定目标日历地址，无法定位事件。");
        }

        return CalDavXmlParser.ResolveUrl(baseUrl, eventUrl);
    }

    private CalDavConfig GetConfig()
    {
        CalDavConfig? config;
        try
        {
            config = _configProvider();
        }
        catch (Exception ex)
        {
            throw new CalDavException("读取 iCloud 配置失败，请打开设置页重新保存配置。", ex);
        }

        if (config is null)
        {
            throw new CalDavException("尚未配置 iCloud 日历账号。");
        }

        if (!config.IsConfigured)
        {
            throw new CalDavException("尚未配置 iCloud 账号：请在设置中填写 Apple ID 邮箱与应用专用密码。");
        }

        if (string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            throw new CalDavException("iCloud CalDAV 服务器地址为空，请在设置中填写。");
        }

        return config;
    }
}
