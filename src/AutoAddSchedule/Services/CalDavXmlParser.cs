using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace AutoAddSchedule.Services;

/// <summary>
/// CalDAV 207 Multi-Status XML 解析工具。全部为无状态的纯函数，便于离线单元测试。
/// </summary>
public static class CalDavXmlParser
{
    /// <summary>DAV 命名空间。</summary>
    public static readonly XNamespace Dav = "DAV:";

    /// <summary>CalDAV 命名空间。</summary>
    public static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    /// <summary>multistatus 中一条 DAV:response 的摘要。</summary>
    public sealed class DavResponse
    {
        /// <summary>服务器返回的 href（通常是相对路径）。</summary>
        public string Href { get; init; } = string.Empty;

        /// <summary>D:displayname，可能为空。</summary>
        public string? DisplayName { get; init; }

        /// <summary>resourcetype 是否包含 C:calendar。</summary>
        public bool IsCalendar { get; init; }

        /// <summary>
        /// 是否支持 VEVENT 组件。服务器未返回 supported-calendar-component-set 时为 null，
        /// 表示无法判断，调用方不应因此排除该日历。
        /// </summary>
        public bool? SupportsEvents { get; init; }
    }

    /// <summary>解析 PROPFIND 207 响应，返回全部 DAV:response 摘要。</summary>
    public static IReadOnlyList<DavResponse> ParseMultiStatus(string xml)
    {
        var document = ParseDocument(xml);
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "multistatus", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("CalDAV 响应根节点不是 multistatus，无法解析。");
        }

        var result = new List<DavResponse>();
        foreach (var response in root.Elements(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value?.Trim();
            if (string.IsNullOrEmpty(href))
            {
                continue;
            }

            string? displayName = null;
            var isCalendar = false;
            bool? supportsEvents = null;

            foreach (var propstat in response.Elements(Dav + "propstat"))
            {
                if (!IsSuccessStatus(propstat.Element(Dav + "status")?.Value))
                {
                    continue;
                }

                var prop = propstat.Element(Dav + "prop");
                if (prop is null)
                {
                    continue;
                }

                if (displayName is null)
                {
                    var value = prop.Element(Dav + "displayname")?.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        displayName = value.Trim();
                    }
                }

                var resourceType = prop.Element(Dav + "resourcetype");
                if (resourceType is not null && resourceType.Elements(CalDav + "calendar").Any())
                {
                    isCalendar = true;
                }

                var componentSet = prop.Element(CalDav + "supported-calendar-component-set");
                if (componentSet is not null)
                {
                    var components = componentSet
                        .Elements(CalDav + "comp")
                        .Select(element => (string?)element.Attribute("name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                        .ToList();
                    if (components.Count > 0)
                    {
                        supportsEvents = components.Any(name =>
                            string.Equals(name, "VEVENT", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            result.Add(new DavResponse
            {
                Href = href,
                DisplayName = displayName,
                IsCalendar = isCalendar,
                SupportsEvents = supportsEvents
            });
        }

        return result;
    }

    /// <summary>从 PROPFIND 响应中取出 current-user-principal 的 href；未找到返回 null。</summary>
    public static string? ParseCurrentUserPrincipalHref(string xml)
    {
        return ParseHrefProperty(xml, Dav + "current-user-principal");
    }

    /// <summary>从 PROPFIND 响应中取出 calendar-home-set 的 href；未找到返回 null。</summary>
    public static string? ParseCalendarHomeSetHref(string xml)
    {
        return ParseHrefProperty(xml, CalDav + "calendar-home-set");
    }

    /// <summary>
    /// 解析日历列表（Depth:1 的 PROPFIND 响应），返回 resourcetype 含 calendar 的日历。
    /// </summary>
    /// <param name="xml">PROPFIND 响应 XML。</param>
    /// <param name="calendarHomeUrl">calendar-home-set 的绝对 URL，用于把 href 解析为绝对 URL。</param>
    public static IReadOnlyList<CalendarInfo> ParseCalendars(string xml, string calendarHomeUrl)
    {
        var responses = ParseMultiStatus(xml);
        var calendars = new List<CalendarInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var response in responses)
        {
            if (!response.IsCalendar || response.SupportsEvents == false)
            {
                continue;
            }

            var url = ResolveUrl(calendarHomeUrl, response.Href);
            if (!seen.Add(url))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(response.DisplayName)
                ? GetLastSegment(response.Href)
                : response.DisplayName.Trim();

            calendars.Add(new CalendarInfo
            {
                Href = response.Href,
                Url = url,
                DisplayName = displayName
            });
        }

        return calendars;
    }

    /// <summary>把 href 解析为绝对 URL；href 已经是绝对 URL 时原样返回。</summary>
    public static string ResolveUrl(string baseUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            throw new FormatException("CalDAV 地址为空，无法解析。");
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new FormatException("基准地址不是绝对 URL，无法解析 CalDAV 相对地址：" + baseUrl);
        }

        if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = new Uri(baseUri.AbsoluteUri + "/");
        }

        return new Uri(baseUri, href).ToString();
    }

    private static string? ParseHrefProperty(string xml, XName propertyName)
    {
        var document = ParseDocument(xml);
        var property = document.Descendants(propertyName).FirstOrDefault();
        if (property is null)
        {
            return null;
        }

        var href = property.Elements(Dav + "href").FirstOrDefault()?.Value?.Trim();
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    private static XDocument ParseDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new FormatException("CalDAV 响应内容为空，无法解析。");
        }

        try
        {
            return XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new FormatException("CalDAV 响应不是合法的 XML：" + ex.Message, ex);
        }
    }

    private static bool IsSuccessStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            // 少数服务器省略 propstat 的 status，按成功处理，避免丢掉属性。
            return true;
        }

        var parts = status.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return true;
        }

        return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code is >= 200 and < 300
            : true;
    }

    private static string GetLastSegment(string href)
    {
        var trimmed = href.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        var segment = index >= 0 ? trimmed[(index + 1)..] : trimmed;
        if (string.IsNullOrEmpty(segment))
        {
            return href;
        }

        try
        {
            return Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException)
        {
            return segment;
        }
    }
}
