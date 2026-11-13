using System.Text.Json.Serialization;

namespace AutoAddSchedule.Core;

public enum ScheduleSource
{
    /// <summary>截图 + 大模型提取。</summary>
    Screenshot = 0,

    /// <summary>手动添加。</summary>
    Manual = 1
}

/// <summary>大模型或手动录入得到的日程草稿（尚未落库）。</summary>
public sealed class ScheduleDraft
{
    public string Title { get; set; } = string.Empty;

    /// <summary>日程开始时间（本地时间）。若截图中有集合/到场/签到时间，用它作为开始时间。</summary>
    public DateTime StartLocal { get; set; }

    public DateTime? EndLocal { get; set; }

    public string? Location { get; set; }

    public string? Description { get; set; }

    /// <summary>提前提醒分钟数。</summary>
    public int ReminderMinutesBefore { get; set; }

    public bool AllDay { get; set; }
}

/// <summary>大模型提取结果：结构化草稿 + 原始返回内容（便于排查）。</summary>
public sealed class ExtractionResult
{
    public required ScheduleDraft Draft { get; init; }

    public string RawJson { get; init; } = string.Empty;

    public string? Model { get; init; }
}

/// <summary>一条本地日程。Uid / RemoteHref / ETag 用于与 iCloud 同步。</summary>
public sealed class ScheduleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>iCalendar UID（创建后不再变化）。</summary>
    public string Uid { get; set; } = Guid.NewGuid().ToString("D");

    public string Title { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? Description { get; set; }

    public DateTime StartLocal { get; set; }

    public DateTime? EndLocal { get; set; }

    public int ReminderMinutesBefore { get; set; }

    public bool AllDay { get; set; }

    public ScheduleSource Source { get; set; }

    public string? ScreenshotPath { get; set; }

    public string? Supplement { get; set; }

    public string? RawLlmJson { get; set; }

    /// <summary>同步目标日历的集合地址。</summary>
    public string? CalendarHref { get; set; }

    public string? CalendarName { get; set; }

    /// <summary>iCloud 上的事件地址（绝对 URL）。</summary>
    public string? RemoteHref { get; set; }

    public string? ETag { get; set; }

    public bool IsSynced { get; set; }

    public string? SyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>最近一次提醒弹窗时间，避免重复提醒。</summary>
    public DateTime? LastNotifiedAt { get; set; }

    /// <summary>软删除标记；同步删除 iCloud 成功后才会真正移除。</summary>
    public DateTime? DeletedAt { get; set; }

    [JsonIgnore]
    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>提醒触发时间 = 开始时间 − 提前分钟数。</summary>
    [JsonIgnore]
    public DateTime ReminderLocal => StartLocal.AddMinutes(-ReminderMinutesBefore);

    public static ScheduleItem FromDraft(ScheduleDraft draft, ScheduleSource source)
    {
        return new ScheduleItem
        {
            Title = string.IsNullOrWhiteSpace(draft.Title) ? "未命名日程" : draft.Title.Trim(),
            StartLocal = draft.StartLocal,
            EndLocal = draft.EndLocal,
            Location = string.IsNullOrWhiteSpace(draft.Location) ? null : draft.Location.Trim(),
            Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim(),
            ReminderMinutesBefore = Math.Max(0, draft.ReminderMinutesBefore),
            AllDay = draft.AllDay,
            Source = source
        };
    }
}
