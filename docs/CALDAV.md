# iCloud / CalDAV 同步

AutoAddSchedule 通过标准 CalDAV（RFC 4791）把本地日程同步到 iCloud 日历，事件内容按 RFC 5545 生成。
除「同步 iCloud」外，本模块的其余逻辑（iCalendar 生成、XML 解析、同步编排）都可离线运行与测试。

## 代码位置

- Services/CalendarBuilder.cs：iCalendar 生成；ICalendarFormat 为可单测的转义 / 折行 / 时间格式化辅助类。
- Services/CalDavClient.cs：HTTP Basic 认证、日历发现、事件创建 / 更新 / 删除。
- Services/CalDavXmlParser.cs：207 Multi-Status 响应解析（无状态纯函数，便于离线测试）。
- Services/SyncService.cs：本地日程与 iCloud 的同步编排、同步状态写回。

## 认证

- 服务器：https://caldav.icloud.com（CalDavConfig.ServerUrl）。
- 用户名：Apple ID 账号（CalDavConfig.AppleId）。账号是邮箱时直接填邮箱；账号是手机号时，CalDAV 用户名必须写成「86 + 手机号」且不带加号，例如手机号 13593178488 写作 8613593178488（实测：纯手机号或带 +86 前缀都会返回 401）。
- 密码：iCloud 应用专用密码（CalDavConfig.AppPassword）。密码经 DPAPI 加密保存在
  %AppData%\AutoAddSchedule\secrets.dat；首次运行可从仓库根目录 .env 的 APPLEID / APPLEKEY 导入。
- 认证方式：HTTP Basic，每个请求都带 Authorization 头。
- 应用专用密码在 https://appleid.apple.com 生成，形如 xxxx-xxxx-xxxx-xxxx，**不能**使用 Apple ID 登录密码。
- 国内 iCloud 账号的日历可能位于分区主机（如 p218-caldav.icloud.com.cn），客户端始终使用服务器返回的绝对 URL，不写死主机名。
- 日志与异常信息只包含请求方法、URL、HTTP 状态码与业务描述，不包含 Authorization 头或密码。

## 日历发现流程

1. PROPFIND {ServerUrl}/（Depth:0），请求 DAV:current-user-principal。
2. PROPFIND {current-user-principal 的 href}（Depth:0），请求 CalDAV:calendar-home-set。
3. PROPFIND {calendar-home-set 的 href}（Depth:1），请求 D:displayname、D:resourcetype、
   CalDAV:supported-calendar-component-set。

解析规则：

- resourcetype 必须包含 CalDAV:calendar。
- 若服务器返回了 supported-calendar-component-set，且其中没有 VEVENT（例如 iCloud「提醒事项」对应的
  VTODO 日历），则跳过该集合；未返回组件集合时不排除。
- displayname 为空时回退为 href 的最后一段（URL 解码后）。
- 同一 URL 只返回一次；homeset 本身（非日历集合）不会出现在结果中。

返回的 CalendarInfo：

- Href：服务器返回的原始 href（通常是相对路径，如 /123456789/calendars/work/）。
- Url：绝对 URL，例如 https://caldav.icloud.com/123456789/calendars/work/。
- DisplayName：显示名。

## URL 约定

- CalDavConfig.CalendarHref 保存选定日历的**绝对 URL**（由设置界面写入）。
- CalDavConfig.CalendarDisplayName 保存显示名，仅用于界面展示。
- 创建事件：PUT {calendar.Url}/{uid}.ics，uid 为 ScheduleItem.Uid。calendar.Url 末尾是否带斜杠均可，
  客户端会规范化，不会产生 //。
- 更新 / 删除事件：使用创建成功后保存到 ScheduleItem.RemoteHref 的绝对 URL。
- 服务器返回 301 / 302 / 303 / 307 / 308 时，客户端手动跟随重定向（最多 3 次）并重新携带 Authorization，
  不使用 HttpClient 的自动重定向（自动重定向会丢弃 Authorization，而 iCloud 可能先跳转到分区主机）。

## 事件写入

- 创建：HTTP PUT，If-None-Match: *，Content-Type: text/calendar; charset=utf-8。
  响应 2xx（iCloud 通常返回 201 Created）视为成功，并从响应头读取 ETag 保存。
- 更新：HTTP PUT 同一事件 URL；If-Match 使用保存的 ETag，ETag 缺失时使用 *。
  响应 2xx（通常是 204 No Content）视为成功，响应若无 ETag 则沿用旧值。
- 删除：HTTP DELETE；有 ETag 时携带 If-Match。远端事件已不存在（404）按删除成功处理，
  避免本地已删除日程因远端被其他设备删掉而永远无法清理。
- 测试连接：PROPFIND 服务器根（Depth:0）验证凭据；2xx（207 Multi-Status）视为成功。

## iCalendar 格式（RFC 5545）

头部固定为：

```text
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//AutoAddSchedule//CN
CALSCALE:GREGORIAN
```

VEVENT 字段与规则：

- UID：ScheduleItem.Uid（创建后不再变化）。
- DTSTAMP：生成时的当前 UTC 时间，格式 yyyyMMddTHHmmssZ。
- DTSTART / DTEND：本地时间转换为 UTC（yyyyMMddTHHmmssZ），写入时不做 VTIMEZONE，避免兼容问题。
  未填写结束时间时 DTEND = DTSTART + 30 分钟；结束时间不晚于开始时间时同样按开始 + 30 分钟修正。
- SUMMARY：日程标题（空标题回退为「未命名日程」）。
- LOCATION：地点非空时写入。
- DESCRIPTION：描述非空时写入。
- 提醒：ReminderMinutesBefore > 0 时追加 VALARM（ACTION:DISPLAY、TRIGGER:-PT{n}M、DESCRIPTION:标题）；
  等于 0 时不写 VALARM。
- 全天事件：DTSTART;VALUE=DATE:yyyyMMdd 与 DTEND;VALUE=DATE:次日，不做时区转换（当前按单日全天事件处理）。

文本与行格式：

- TEXT 值转义反斜杠（\\）、分号（\;）、逗号（\,），换行（CRLF / CR / LF）统一转为 \n。
- 每个物理行不超过 75 字节（UTF-8）；超长行按字符边界折行，续行以单个空格开头，不会把中文等多字节字符拆开。
- 全部物理行使用 CRLF 行尾，最后一行同样以 CRLF 结束。

## 同步服务行为

PushAsync（推送单条日程）：

1. CalDavConfig 未配置（缺 Apple ID 或应用专用密码）→ 失败，提示「尚未配置 iCloud 账号…」。
2. CalendarHref 为空 → 失败，提示「尚未配置 iCloud 日历…」。
3. ScheduleItem.RemoteHref 为空 → 调用创建接口；成功后写回 RemoteHref、ETag、CalendarHref、CalendarName、
   IsSynced = true、LastSyncedAt，并通过 ScheduleStore 保存。
4. RemoteHref 非空 → 调用更新接口；成功后更新 ETag、IsSynced、LastSyncedAt。
5. 任意异常 → 返回中文失败信息，写 item.SyncError、IsSynced = false 并保存，同时写日志。

DeleteRemoteAsync（删除远端事件）：

- RemoteHref 为空 → 直接成功（无需删除）。
- 未配置账号 → 失败。
- 删除成功后清空 RemoteHref / ETag，IsSynced = false。
- 失败时写 item.SyncError 并写日志。

PushPendingAsync（批量推送待同步项）：

- 遍历 ScheduleStore.GetAll(includeDeleted: true)。
- 已软删除项：先删除远端事件，成功后再 RemoveHard 从本地彻底移除。
- 未同步项（!IsSynced）：调用 PushAsync。
- 单项失败不中断其余项；返回成功条数（含成功删除的条数）。

同步状态字段：IsSynced、SyncError、RemoteHref、ETag、CalendarHref、CalendarName、LastSyncedAt。

## 错误映射

- HTTP 401：认证失败：Apple ID 或应用专用密码不正确。请确认 Apple ID 邮箱，并使用应用专用密码（不是登录密码）。
- HTTP 403：iCloud 拒绝访问（403）：请确认账号已开启双重认证，并重新生成应用专用密码。
- HTTP 404：找不到 CalDAV 资源（404）：服务器地址或日历地址可能已失效，请重新读取日历列表。
- HTTP 409：iCloud 返回冲突（409）：该日程可能已被其他设备修改，请刷新后重试。
- HTTP 412：iCloud 返回前置条件失败（412）：该日程已在其他设备上被修改，请刷新后重试。
- HTTP 429：请求过于频繁（429）：iCloud 暂时限制访问，请稍后重试。
- HTTP 5xx：iCloud 服务暂时不可用（HTTP xxx），请稍后重试。
- 请求超时：连接 iCloud 超时，请检查网络后重试。
- 网络异常：无法连接 iCloud 服务，请检查网络连接或代理设置。
- XML 解析失败：iCloud 返回的 CalDAV 数据无法解析，请稍后重试或检查服务器地址。
- 未配置：尚未配置 iCloud 账号 / 尚未配置 iCloud 日历，请先在设置中完成配置。


## 离线测试

- tests/AutoAddSchedule.Tests/CalendarBuilderTests.cs：iCalendar 转义、75 字节折行（含中文多字节字符）、
  UTC 转换、默认结束时间、VALARM 有 / 无、全天事件、CRLF 行尾、长描述折行。
- tests/AutoAddSchedule.Tests/CalDavClientTests.cs：
  - CalDavClient 用假 HttpMessageHandler 离线验证 PROPFIND 请求（方法、URL、Depth、Basic 认证）、
    日历发现三步流程、PUT / DELETE 的 If-Match / If-None-Match 与 Content-Type、错误映射、配置缺失提示。
  - CalDavXmlParserTests 用离线样例 XML 验证多状态、多日历、displayname / href / 绝对 URL 解析、
    VTODO 日历过滤、重定向 URL 解析与非法 XML 处理。
- 上述测试不访问真实 iCloud，不创建 / 删除真实事件。

## 真实 iCloud 验证（默认跳过）

- 只读验证：tests/AutoAddSchedule.Tests/LiveEnvironmentTests.cs（连接 + 读取日历列表），由环境变量
  AUTOADD_SCHEDULE_LIVE_CALDAV=1 开启。
- 写入验证：tests/AutoAddSchedule.Tests/LiveCalDavWriteTests.cs（创建 → PROPFIND 校验 → 删除 → 复查 404），
  由环境变量 AUTOADD_SCHEDULE_LIVE_CALDAV_WRITE=1 与 AUTOADD_SCHEDULE_LIVE_CALDAV_URL（目标日历绝对 URL）开启。
  该测试会真实写入一条测试事件并在验证后删除，仅在人工确认后运行。
- 交付前实测：手机号 Apple ID 账号可以正常发现 3 个日历；在「个人」日历创建事件成功、PROPFIND 返回 207、
  删除后返回 404；应用内「截图 → 大模型提取 → 创建到 iCloud」整条链路成功，列表显示「已同步」。