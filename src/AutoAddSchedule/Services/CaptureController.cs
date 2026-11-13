using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 串联完整链路：截图选区 → 保存截图 → 大模型提取 → 本地存储 → 同步 iCloud → 通知。
/// 必须在 UI 线程调用 <see cref="RunAsync"/>。
/// </summary>
public sealed class CaptureController
{
    private bool _running;

    public bool IsRunning => _running;

    public async Task RunAsync()
    {
        if (_running)
        {
            AppLog.Warn("截图流程仍在进行中，忽略重复触发。");
            return;
        }

        _running = true;
        try
        {
            var capture = AppServices.CaptureOverlay.ShowCapture();
            if (capture is null)
            {
                AppLog.Info("用户放弃了本次截图。");
                return;
            }

            var screenshotPath = await AppServices.Screenshots.SaveAsync(capture.Image);
            AppServices.Screenshots.Cleanup();

            AppServices.Notifications.Show("正在识别日程", "已收到截图，正在调用大模型提取日程…");

            ExtractionResult extraction;
            try
            {
                extraction = await AppServices.Extractor.ExtractAsync(screenshotPath, capture.Supplement);
            }
            catch (Exception ex)
            {
                AppLog.Error("大模型提取日程失败", ex);
                AppServices.Notifications.Show(
                    "日程提取失败",
                    ex.Message + Environment.NewLine + "截图已保存：" + screenshotPath,
                    NotificationKind.Error);
                return;
            }

            var item = ScheduleItem.FromDraft(extraction.Draft, ScheduleSource.Screenshot);
            item.ScreenshotPath = screenshotPath;
            item.Supplement = capture.Supplement;
            item.RawLlmJson = extraction.RawJson;
            AppServices.Schedules.Add(item);

            var caldav = AppServices.Config.Config.CalDav;
            if (caldav.IsConfigured && !string.IsNullOrWhiteSpace(caldav.CalendarHref))
            {
                var outcome = await AppServices.Sync.PushAsync(item);
                if (outcome.Synced)
                {
                    AppServices.Notifications.Show(
                        "已添加日程",
                        BuildSummary(item) + Environment.NewLine + "已同步到 iCloud 日历。");
                }
                else
                {
                    AppServices.Notifications.Show(
                        "已保存到本地，同步 iCloud 失败",
                        BuildSummary(item) + Environment.NewLine + (outcome.Error ?? "未知错误"),
                        NotificationKind.Warning);
                }
            }
            else
            {
                AppServices.Notifications.Show(
                    "已添加日程（仅本地）",
                    BuildSummary(item) + Environment.NewLine + "尚未配置 iCloud 日历，联网配置后可手动同步。",
                    NotificationKind.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("截图添加日程流程失败", ex);
            AppServices.Notifications.Show("操作失败", ex.Message, NotificationKind.Error);
        }
        finally
        {
            _running = false;
        }
    }

    private static string BuildSummary(ScheduleItem item)
    {
        var text = item.Title + Environment.NewLine + "开始：" + TimeFormat.FormatDateTime(item.StartLocal);
        if (item.ReminderMinutesBefore > 0)
        {
            text += Environment.NewLine + "提醒：" + TimeFormat.FormatDateTime(item.ReminderLocal) +
                    "（提前 " + item.ReminderMinutesBefore + " 分钟）";
        }

        return text;
    }
}
