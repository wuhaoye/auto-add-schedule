using System.Globalization;
using System.Text.Json;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 解析大模型返回的日程 JSON。容错处理 Markdown 代码块包裹、前后多余文字、字段缺失与时间格式差异，
/// 校验失败时抛出中文异常（由调用方提示用户并记录日志）。
/// </summary>
public static class ScheduleJsonParser
{
    private static readonly string[] TimeFormats =
    {
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy-M-d H:mm:ss",
        "yyyy-M-d H:mm",
        "yyyy-M-d",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy/MM/dd",
        "yyyy/M/d H:mm:ss",
        "yyyy/M/d H:mm",
        "yyyy/M/d",
        "yyyy年M月d日 HH:mm:ss",
        "yyyy年M月d日 HH:mm",
        "yyyy年M月d日H:mm",
        "yyyy年M月d日"
    };

    /// <summary>
    /// 把模型原始输出解析为 <see cref="ScheduleDraft"/>。
    /// </summary>
    /// <exception cref="InvalidOperationException">内容为空、不是合法 JSON、缺少必填字段或时间非法。</exception>
    public static ScheduleDraft Parse(string? raw)
    {
        var json = ExtractJsonObject(raw);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("大模型返回的内容不是合法 JSON：" + ex.Message, ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("大模型返回的内容不是 JSON 对象。");
            }

            root = UnwrapIfNeeded(root);

            var title = ReadString(root, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("大模型返回的内容缺少日程标题（title）。");
            }

            var startText = ReadString(root, "start");
            if (string.IsNullOrWhiteSpace(startText))
            {
                throw new InvalidOperationException("大模型返回的内容缺少开始时间（start）。");
            }

            var start = ParseTime(startText, "开始时间");
            bool allDay;
            var allDayValue = ReadBool(root, "all_day");
            if (allDayValue.HasValue)
            {
                allDay = allDayValue.Value;
            }
            else
            {
                allDay = IsDateOnlyText(startText);
            }

            if (allDay)
            {
                start = start.Date;
            }

            DateTime? end = null;
            var endText = ReadString(root, "end");
            if (!string.IsNullOrWhiteSpace(endText))
            {
                end = ParseTime(endText, "结束时间");
                if (end.Value < start)
                {
                    throw new InvalidOperationException("大模型返回的结束时间早于开始时间。");
                }

                if (allDay)
                {
                    end = end.Value.Date;
                }
            }

            var reminder = ReadInt(root, "reminder_minutes_before") ?? 0;
            if (reminder < 0)
            {
                reminder = 0;
            }

            return new ScheduleDraft
            {
                Title = title.Trim(),
                StartLocal = start,
                EndLocal = end,
                Location = TrimToNull(ReadString(root, "location")),
                Description = TrimToNull(ReadString(root, "description")),
                ReminderMinutesBefore = reminder,
                AllDay = allDay
            };
        }
    }

    /// <summary>去掉 Markdown 代码块标记，并截取第一个 { 到最后一个 } 之间的 JSON 对象。</summary>
    private static string ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("大模型返回内容为空。");
        }

        var text = raw.Trim().TrimStart('\uFEFF').Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            text = firstLineEnd >= 0 ? text[(firstLineEnd + 1)..] : text[3..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("大模型返回的内容中没有 JSON 对象。");
        }

        return text[start..(end + 1)];
    }

    /// <summary>兼容模型把结果包在 schedule / event / data 等外层对象里的情况。</summary>
    private static JsonElement UnwrapIfNeeded(JsonElement root)
    {
        if (HasProperty(root, "title"))
        {
            return root;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && HasProperty(property.Value, "title"))
            {
                return property.Value;
            }
        }

        return root;
    }

    private static DateTime ParseTime(string value, string fieldName)
    {
        var text = value.Trim();
        if (DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        // 带时区偏移的时间按“墙上时间”处理，避免被转换成本机时区后日期漂移。
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offsetTime))
        {
            return offsetTime.DateTime;
        }

        throw new InvalidOperationException("无法解析" + fieldName + "：" + text);
    }

    private static bool IsDateOnlyText(string value)
    {
        var text = value.Trim();
        return text.IndexOf(':') < 0 && text.IndexOf('T') < 0;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool HasProperty(JsonElement element, string name) => TryGetProperty(element, name, out _);

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                var text = value.GetString();
                if (bool.TryParse(text, out var parsed))
                {
                    return parsed;
                }

                return text?.Trim() == "1" ? true : text?.Trim() == "0" ? false : null;
            case JsonValueKind.Number:
                return value.TryGetInt32(out var number) ? number != 0 : null;
            default:
                return null;
        }
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt32(out var number))
                {
                    return number;
                }

                return value.TryGetDouble(out var doubleValue) ? (int)Math.Round(doubleValue) : null;
            case JsonValueKind.String:
                return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
