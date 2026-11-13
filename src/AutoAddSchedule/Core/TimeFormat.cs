namespace AutoAddSchedule.Core;

/// <summary>时间显示与测试比对使用的统一格式。</summary>
public static class TimeFormat
{
    /// <summary>提醒触发时间格式：2026年10月26日6：45（全角冒号，小时不补零）。</summary>
    public static string FormatReminderTime(DateTime localTime)
    {
        return localTime.ToString("yyyy") + "年" +
               localTime.Month + "月" +
               localTime.Day + "日" +
               localTime.Hour + "：" +
               localTime.Minute.ToString("D2");
    }

    /// <summary>常规时间：2026-10-26 06:45</summary>
    public static string FormatDateTime(DateTime localTime) => localTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>友好时间：10月26日 06:45</summary>
    public static string FormatFriendly(DateTime localTime) => localTime.ToString("M月d日 HH:mm");

    /// <summary>日期：2026-10-26</summary>
    public static string FormatDate(DateTime localTime) => localTime.ToString("yyyy-MM-dd");
}
