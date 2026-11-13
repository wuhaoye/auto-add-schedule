using System;

namespace AutoAddSchedule.Services;

/// <summary>
/// 大模型日程提取的提示词。规则集中在这里，便于离线测试与后续调整。
/// </summary>
public static class LlmPrompt
{
    private static readonly string[] WeekdayNames = { "日", "一", "二", "三", "四", "五", "六" };

    /// <summary>
    /// 系统提示词：注入当前日期时间，约束严格 JSON 输出与时间提取规则。
    /// </summary>
    public static string BuildSystemPrompt(DateTime now)
    {
        var weekday = "星期" + WeekdayNames[(int)now.DayOfWeek];
        var currentTime = now.ToString("yyyy-MM-dd HH:mm:ss");
        return $$"""
            你是一个严谨的日程信息提取助手。用户会提供一张截图（可能是通知、聊天记录、海报或日程邀请），并可能附带一段补充信息。请从截图中提取一个最相关的日程，只输出一个 JSON 对象。

            当前日期时间：{{currentTime}}（{{weekday}}，年份 {{now.Year}}）。截图中若只有月日而没有年份，使用当前年份 {{now.Year}}。

            输出字段（只允许以下字段，内容使用简体中文；无法确定的字符串用 null）：
            {
              "title": "日程标题，简洁明确，如“参观校史馆”",
              "start": "开始时间，格式 yyyy-MM-ddTHH:mm:ss",
              "end": "结束时间，格式 yyyy-MM-ddTHH:mm:ss，无法确定用 null",
              "location": "地点，无法确定用 null",
              "description": "简要说明，无法确定用 null",
              "reminder_minutes_before": 0,
              "all_day": false
            }

            时间规则（按顺序判断）：
            1. 若截图明确给出「集合时间 / 到场时间 / 签到时间」的具体钟点（例如“集合时间：10月26日上午6:45”），把该时间作为 start；此时 reminder_minutes_before 默认为 0。
            2. 若截图只给出「提前 X 分钟集合 / 到场 / 签到」这类相对提前量（例如“请提前10分钟到场签到”），start 取活动开始时间，reminder_minutes_before 取 X。
            3. 其他情况下 start 取活动开始时间，reminder_minutes_before 默认为 0。
            4. 若只有日期没有具体时间，all_day 取 true，start 取当天 00:00:00。
            5. end 不得早于 start；结束时间无法确定时用 null。

            补充信息规则：
            - 补充信息是用户的额外说明，优先级高于截图内容。
            - 补充信息中「提前 X 分钟提醒我」之类的提醒要求，覆盖上面所有 reminder_minutes_before 的取值。
            - 补充信息中明确给出的标题、时间、地点等，覆盖截图中的对应内容。

            其他要求：
            - 不要编造截图和补充信息中都没有的信息；无法确定的字段用 null。
            - title 必须非空；start 必须是合法时间。
            - 只输出 JSON 对象本身，不要输出 Markdown 代码块标记、解释或任何多余文字。
            """;
    }

    /// <summary>
    /// 用户提示词：截图说明 + 可选补充信息。补充信息优先级最高。
    /// </summary>
    public static string BuildUserPrompt(string? supplement)
    {
        if (string.IsNullOrWhiteSpace(supplement))
        {
            return "请从这张截图中提取日程信息。补充信息：无。";
        }

        return "请从这张截图中提取日程信息。\r\n补充信息（优先级最高，其中“提前 X 分钟提醒我”会覆盖截图中的提前量）：\r\n" + supplement.Trim();
    }
}
