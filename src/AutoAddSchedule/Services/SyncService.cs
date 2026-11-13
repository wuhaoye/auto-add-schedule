using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 本地日程与 iCloud 日历之间的同步服务。
/// 全部通过 <see cref="AppServices"/> 取依赖，界面无需感知底层 CalDAV 细节。
/// </summary>
public sealed class SyncService : ISyncService
{
    private const string NotConfiguredMessage =
        "尚未配置 iCloud 账号：请在设置中填写 Apple ID 邮箱与应用专用密码。";

    private const string NoCalendarMessage =
        "尚未配置 iCloud 日历：请先在设置中读取日历列表并选择要同步的日历。";

    public async Task<SyncOutcome> PushAsync(ScheduleItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var caldav = AppServices.Config.Config.CalDav;
        if (!caldav.IsConfigured)
        {
            return SyncOutcome.Failure(NotConfiguredMessage);
        }

        if (string.IsNullOrWhiteSpace(caldav.CalendarHref))
        {
            return SyncOutcome.Failure(NoCalendarMessage);
        }

        try
        {
            var calendar = BuildCalendarInfo(caldav);
            var ics = AppServices.CalendarBuilder.BuildEvent(item);
            var isNew = string.IsNullOrWhiteSpace(item.RemoteHref);

            if (isNew)
            {
                var result = await AppServices.CalDav
                    .CreateEventAsync(calendar, ics, item.Uid, cancellationToken)
                    .ConfigureAwait(false);

                item.RemoteHref = string.IsNullOrWhiteSpace(result.Href) ? null : result.Href;
                item.ETag = result.ETag;
                item.CalendarHref = caldav.CalendarHref;
                item.CalendarName = caldav.CalendarDisplayName;
                MarkSynced(item);
                AppLog.Info("日程「" + item.Title + "」已创建到 iCloud 日历「" + calendar.DisplayName + "」。");
                return SyncOutcome.Success("已同步到 iCloud 日历。");
            }

            var updateResult = await AppServices.CalDav
                .UpdateEventAsync(calendar, item.RemoteHref!, ics, item.ETag, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(updateResult.ETag))
            {
                item.ETag = updateResult.ETag;
            }

            item.CalendarHref = caldav.CalendarHref;
            item.CalendarName = caldav.CalendarDisplayName;
            MarkSynced(item);
            AppLog.Info("日程「" + item.Title + "」已更新到 iCloud 日历。");
            return SyncOutcome.Success("已更新 iCloud 日历上的日程。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SyncOutcome.Failure("同步已取消。");
        }
        catch (Exception ex)
        {
            item.IsSynced = false;
            item.SyncError = ex.Message;
            TryUpdate(item);
            AppLog.Error("同步日程「" + item.Title + "」到 iCloud 失败", ex);
            return SyncOutcome.Failure("同步到 iCloud 失败：" + ex.Message);
        }
    }

    public async Task<SyncOutcome> DeleteRemoteAsync(ScheduleItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.RemoteHref))
        {
            return SyncOutcome.Success("该日程没有 iCloud 远端记录，无需删除。");
        }

        var caldav = AppServices.Config.Config.CalDav;
        if (!caldav.IsConfigured)
        {
            return SyncOutcome.Failure(NotConfiguredMessage);
        }

        try
        {
            var calendar = BuildCalendarInfo(caldav);
            await AppServices.CalDav
                .DeleteEventAsync(calendar, item.RemoteHref, item.ETag, cancellationToken)
                .ConfigureAwait(false);

            item.RemoteHref = null;
            item.ETag = null;
            item.IsSynced = false;
            item.SyncError = null;
            TryUpdate(item);
            AppLog.Info("日程「" + item.Title + "」已从 iCloud 日历删除。");
            return SyncOutcome.Success("已从 iCloud 日历删除该日程。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SyncOutcome.Failure("删除已取消。");
        }
        catch (Exception ex)
        {
            item.SyncError = ex.Message;
            TryUpdate(item);
            AppLog.Error("从 iCloud 删除日程「" + item.Title + "」失败", ex);
            return SyncOutcome.Failure("从 iCloud 删除日程失败：" + ex.Message);
        }
    }

    public async Task<int> PushPendingAsync(CancellationToken cancellationToken = default)
    {
        var items = AppServices.Schedules.GetAll(includeDeleted: true);
        var success = 0;

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AppLog.Warn("待推送同步已取消。");
                break;
            }

            try
            {
                if (item.IsDeleted)
                {
                    // 已软删除：先删除远端事件，成功后再从本地彻底移除。
                    var outcome = await DeleteRemoteAsync(item, cancellationToken).ConfigureAwait(false);
                    if (outcome.Synced)
                    {
                        AppServices.Schedules.RemoveHard(item.Id);
                        success++;
                    }
                }
                else if (!item.IsSynced)
                {
                    var outcome = await PushAsync(item, cancellationToken).ConfigureAwait(false);
                    if (outcome.Synced)
                    {
                        success++;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单项失败不中断其余日程。
                AppLog.Error("同步日程「" + item.Title + "」时出错", ex);
            }
        }

        AppLog.Info("待推送同步完成：检查 " + items.Count + " 条，成功 " + success + " 条。");
        return success;
    }

    private static CalendarInfo BuildCalendarInfo(CalDavConfig caldav)
    {
        var href = caldav.CalendarHref!;
        var url = Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : CalDavXmlParser.ResolveUrl(caldav.ServerUrl, href);

        return new CalendarInfo
        {
            Href = href,
            Url = url,
            DisplayName = caldav.CalendarDisplayName ?? string.Empty
        };
    }

    private static void MarkSynced(ScheduleItem item)
    {
        item.IsSynced = true;
        item.SyncError = null;
        item.LastSyncedAt = DateTime.Now;
        TryUpdate(item);
    }

    private static void TryUpdate(ScheduleItem item)
    {
        try
        {
            AppServices.Schedules.Update(item);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存日程同步状态失败", ex);
        }
    }
}
