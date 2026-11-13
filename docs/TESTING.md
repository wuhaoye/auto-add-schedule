# 测试

## 测试项目

tests/AutoAddSchedule.Tests（xUnit，目标 net10.0-windows，引用主程序 AutoAddSchedule）。
当前共 95 个测试：全部通过（失败 0、跳过 0）。其中 3 个会真实调用 DeepSeek（读取仓库根 .env）；
另有 3 个真实环境验证测试默认跳过（只读 2 个、写入 1 个，见下文）。

## 运行方式

```powershell
# 推荐：内部带构建锁，避免与构建并发冲突
pwsh tools/test.ps1

# 只跑某一类测试
pwsh tools/test.ps1 -Filter "FullyQualifiedName~CalendarBuilderTests"
```

也可以直接 dotnet test tests/AutoAddSchedule.Tests/AutoAddSchedule.Tests.csproj。

## 单元测试（离线）

- 时间格式化：提醒时间格式「2026年10月26日6：45」（全角冒号、小时不补零）。
- 配置与密钥：.env 解析、DPAPI 加解密往返、配置文件解析。
- 大模型：LlmClientTests（请求体、图片 data URI、response_format、401/429/超时/连接失败中文映射）、
  LlmPromptTests、ExtractionJsonParserTests（代码块包裹/缺字段/非法时间）、ExtractionServiceTests（假 ILlmClient）。
- iCalendar：CalendarBuilderTests（TEXT 转义、75 字节折行含中文边界、UTC 转换、默认结束 +30 分钟、
  VALARM 有/无、全天事件、CRLF）。
- CalDAV：CalDavClientTests（假 HttpMessageHandler：PROPFIND 三步发现、PUT/DELETE 的条件请求头、
  ETag 解析、错误映射、重定向、XML 解析）。
- 截图存储：ScreenshotStoreTests（临时目录：命名、不覆盖、不可写异常、保留 N 张、删除数量、下限 1）。

## 集成测试（真实大模型）

LlmIntegrationTests 遍历 test/ 下的每个样本目录（含 image.png）：

- 可选 add 文件作为补充信息；
- 调用真实 DeepSeek（默认 deepseek-flash）提取日程；
- 期望值 = result 文件内容 Trim；
- 实际值 = TimeFormat.FormatReminderTime(草稿开始时间 − 提前分钟数)；
- 无 MODELKEY 时自动跳过。

样本语义：result = 提醒触发时间；add = 补充信息。

当前 3 个样本全部通过：

- test/01 → 2026年10月26日6：45
- test/02 → 2026年10月28日14：50
- test/03（补充信息「提前30提醒我」）→ 2026年11月24日20：15

注意：每次运行集成测试会产生 3 次真实 API 调用（费用极小），不要循环压测。

## 真实环境只读验证（默认跳过）

LiveEnvironmentTests 用于人工验证外部账号连通性，默认直接返回（不执行网络请求）：

```powershell
# iCloud CalDAV 只读验证（TestConnection + 读取日历列表，不写任何事件）
$env:AUTOADD_SCHEDULE_LIVE_CALDAV='1'
# 大模型连接验证（最小请求）
$env:AUTOADD_SCHEDULE_LIVE_LLM='1'
pwsh tools/test.ps1 -Filter "FullyQualifiedName~LiveEnvironmentTests"
```

凭据来自仓库根 .env（MODELKEY / APPLEID / APPLEKEY），不会写入日志或测试输出。

## 真实 iCloud 写入验证（默认跳过）

LiveCalDavWriteTests 会真实创建一条测试事件、用 PROPFIND 确认存在、再删除并确认返回 404：

```powershell
$env:AUTOADD_SCHEDULE_LIVE_CALDAV_WRITE='1'
$env:AUTOADD_SCHEDULE_LIVE_CALDAV_URL='https://p218-caldav.icloud.com.cn/xxx/calendars/yyy/'
pwsh tools/test.ps1 -Filter "FullyQualifiedName~LiveCalDavWriteTests"
```

该测试会真实写入用户日历，仅在人工确认后运行；目标日历 URL 可从程序「设置 → 读取日历列表」获得。

## 交付前实测记录

- 手机号 Apple ID（8613593178488）+ 应用专用密码：只读发现 3 个日历（工作 / 个人 / 哔哩哔哩）。
- 真实写入：在「个人」日历创建事件成功并返回 ETag；PROPFIND 校验 207；删除后复查 404。
- 应用整链路：截图 → deepseek-flash 提取 → 创建到 iCloud「个人」日历 → 列表同步状态显示「已同步」。
- 托盘提醒、日历 / 列表 / 设置界面、自包含发布版启动：均实测正常。
- 费用提示：每次真实大模型集成测试会产生 3 次 DeepSeek 调用（费用极小），不要循环压测。

## 手工/自动界面验收

- tools/ui-smoke.ps1：启动程序 → 截图主界面 → 发送 Ctrl+Alt+\ → 模拟拖选 → 截图遮罩/工具条 →
  （可选）按 Enter 跑通真实识别链路 → 截图结果 → 关闭程序。
  用法示例：pwsh tools/ui-smoke.ps1 -DoCapture（只验证界面）、-DoCapture -ConfirmCapture（含真实调用）。
- 视觉检查截图输出在 .tmp/ui/（已 gitignore）。
- 调度提醒：可在 schedules.json 写入一条 1 分钟后开始的日程后启动程序，等待托盘气泡提醒。