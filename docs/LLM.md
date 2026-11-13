# 大模型提取

## 接口

DeepSeek 使用 OpenAI 兼容协议：POST {BaseUrl}/chat/completions，默认 BaseUrl 为 https://api.deepseek.com。
请求头使用 Authorization: Bearer {ApiKey}，model 取自配置中的「大模型 → 模型」项，请求超时取
「大模型 → 超时秒数」（默认 120 秒，允许范围 5~600 秒）。

- 图片：读取本地截图文件，转成 base64 data URI（如 data:image/png;base64,...），
  放入 user 消息 content 数组：

```json
{
  "type": "image_url",
  "image_url": { "url": "data:image/png;base64,..." }
}
```

- 需要严格 JSON 输出时，请求体带 "response_format": {"type": "json_object"}。
- 响应取 choices[0].message.content 作为模型文本输出。
- 默认模型 deepseek-flash 支持图片输入；deepseek-v4-pro 仅支持文本，不能用于截图识别。

## 提示词（Services/LlmPrompt.cs）

系统提示词注入当前日期时间（含年份与星期），并规定以下内容：

### 输出 JSON 字段

| 字段 | 说明 |
| --- | --- |
| title | 日程标题，必填、非空，简洁明确 |
| start | 开始时间，必填，格式 yyyy-MM-ddTHH:mm:ss |
| end | 结束时间，可空；不得早于 start |
| location | 地点，无法确定用 null |
| description | 简要说明，无法确定用 null |
| reminder_minutes_before | 提前提醒分钟数，默认 0，最小 0 |
| all_day | 是否全天，只有日期没有时间时为 true |

### 时间规则

1. 截图明确给出「集合时间 / 到场时间 / 签到时间」的具体钟点（如“集合时间：10月26日上午6:45”）时，
   用该时间作为 start，此时 reminder_minutes_before 默认 0。
2. 截图只给出「提前 X 分钟集合 / 到场 / 签到」这类相对提前量（如“请提前10分钟到场签到”）时，
   start 取活动开始时间，reminder_minutes_before 取 X。
3. 其他情况 start 取活动开始时间，reminder_minutes_before 默认 0。
4. 截图中年份缺失时使用当前年份（当前年份由系统提示词注入）。
5. 只有日期没有具体时间时 all_day 为 true，start 取当天 00:00:00。

### 补充信息规则

- 补充信息（截图选区的「补充信息」对话框内容）作为额外说明，优先级高于截图内容。
- 补充信息中的「提前 X 分钟提醒我」之类要求覆盖 reminder_minutes_before，包括截图中的提前量。
- 补充信息中明确给出的标题、时间、地点等覆盖截图中的对应内容。
- 无法确定的信息留空（null），不允许编造；只输出 JSON 对象，不输出代码块标记或解释。

## 结果解析与校验（Services/ScheduleJsonParser.cs）

解析时容错处理以下情况：

- 返回内容被 Markdown 代码块标记包裹（json 代码块）；
- JSON 前后带有解释性文字（截取第一个 { 到最后一个 } 之间的内容）；
- 字段名大小写不一致；
- 结果被包在 schedule / event / data 等外层对象中；
- 时间格式差异：yyyy-MM-ddTHH:mm:ss、yyyy-MM-dd HH:mm、yyyy-MM-dd、yyyy/MM/dd、yyyy年M月d日 等。

校验规则：

- title 非空、start 必填且可解析为合法时间，否则抛出中文异常；
- end 可空，但不得早于 start；
- reminder_minutes_before 缺省为 0，负数按 0 处理；
- 仅日期的时间按全天处理（all_day 自动为 true，start 取当天 00:00:00）。

解析失败时，原始返回内容会截断到 2000 字符写入日志（%AppData%\AutoAddSchedule\logs\app.log），
日志与异常信息中不包含 API Key。

## 错误映射（Services/LlmClient.cs）

客户端把网络与接口错误翻译为中文提示，直接用于托盘/弹窗展示：

| 情况 | 提示 |
| --- | --- |
| 未配置 API Key | 尚未配置大模型 API Key，请在设置中填写 DeepSeek API Key。 |
| HTTP 401 | 大模型 API Key 无效或已过期，请在设置中重新填写。 |
| HTTP 403 | 大模型 API Key 无权访问该服务（HTTP 403），请检查 Key 与 Base URL 配置。 |
| HTTP 429 | 请求过于频繁（HTTP 429），请稍后重试。 |
| 请求超时 | 无法连接大模型服务：请求超时（N 秒），请检查网络后重试。 |
| 连接失败 | 无法连接大模型服务：请检查网络连接与 Base URL 配置。 |
| 其它非 2xx | 大模型服务返回错误（HTTP 状态码）：响应内容（截断）。 |
| 响应格式异常 / 内容为空 | 大模型返回内容为空，请稍后重试。 |

## 测试

### 离线单元测试（不联网，不需要 API Key）

- LlmClientTests：用假 HttpMessageHandler 校验请求地址、Authorization、模型名、图片 data URI、
  response_format，以及 401 / 429 / 超时 / 连接失败等错误映射。
- LlmPromptTests：校验当前日期（含年份、星期）注入与提示词中的字段、时间规则、补充信息覆盖规则。
- ExtractionJsonParserTests：校验正常 JSON、json 代码块包裹、前后文字、缺字段、非法时间、全天日期、
  负数提醒、结束时间早于开始时间等情况。
- ExtractionServiceTests：用假 ILlmClient 校验提示词与图片传递、代码块解析、原始返回保留、
  补充信息与提醒值、非法返回的中文异常、截图不存在异常、取消令牌透传。

### 真实接口集成测试

LlmIntegrationTests 遍历仓库 test/ 下的样本目录（包含 image.png 与 result 的目录）：

- 若样本目录存在 add 文件，读取其内容作为补充信息；
- 调用真实 DeepSeek 接口完成提取；
- 期望值 = result 文件内容 Trim；
- 实际值 = TimeFormat.FormatReminderTime(开始时间 − 提前提醒分钟数)；
- 断言两者相等。运行前把 image.png 复制到临时目录，不修改 test/ 下的原始样本。

运行方式：

- 全量：pwsh tools/test.ps1
- 只跑真实接口测试：pwsh tools/test.ps1 -Filter "FullyQualifiedName~LlmIntegrationTests"
- 只跑离线测试：pwsh tools/test.ps1 -Filter "FullyQualifiedName!~LlmIntegrationTests"

集成测试通过 Core/EnvFile.LoadDefault() 从测试程序目录向上查找仓库根目录的 .env，读取 MODELKEY
（可选 MODELBASE / MODEL 覆盖端点与模型），不依赖 AppServices 初始化。没有 MODELKEY 时测试标记为 Skip；
真实样本只有 test/ 下 3 个，运行时每个样本仅调用一次接口，不要循环压测。
