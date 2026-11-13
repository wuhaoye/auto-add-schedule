# 大模型提取

## 接口

DeepSeek 使用 OpenAI 兼容协议：POST {BaseUrl}/chat/completions，图片以 image_url（data URI）传入。
默认模型 deepseek-flash（支持文本 + 图片输入）。

## 提示词要求

- 输入：截图（通知、聊天记录、海报等）+ 可选补充信息 + 当前日期时间。
- 输出：严格 JSON（response_format: {"type":"json_object"}），字段：

```json
{
  "title": "日程标题",
  "start": "2026-10-26T06:45:00",
  "end": "2026-10-26T07:00:00",
  "location": "地点",
  "description": "说明",
  "reminder_minutes_before": 0,
  "all_day": false
}
```

- 规则：时间缺失年份时使用当前年份；图片中有「集合时间 / 到场时间 / 签到时间」时以它为 start；
  补充信息中的提醒要求（如「提前30分钟提醒我」）覆盖 reminder_minutes_before；
  无法确定的信息留空，不要编造。

## 结果校验

- 必须能被 System.Text.Json 解析；title 与 start 必填；时间非法时报错并提示用户。
- 校验失败时保留原始返回内容到日志，便于排查。
