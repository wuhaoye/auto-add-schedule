using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoAddSchedule.Core;

/// <summary>本地日程存储（schedules.json）。线程安全，写入采用临时文件替换。</summary>
public sealed class ScheduleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private List<ScheduleItem> _items = new();

    public event EventHandler? Changed;

    public void Load()
    {
        lock (_gate)
        {
            _items = new List<ScheduleItem>();
            if (!File.Exists(AppPaths.SchedulesFilePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(AppPaths.SchedulesFilePath);
                var loaded = JsonSerializer.Deserialize<List<ScheduleItem>>(json, JsonOptions);
                if (loaded is not null)
                {
                    _items = loaded;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取 schedules.json 失败", ex);
            }
        }
    }

    public IReadOnlyList<ScheduleItem> GetAll(bool includeDeleted = false)
    {
        lock (_gate)
        {
            var query = includeDeleted ? _items : _items.Where(i => i.DeletedAt is null);
            return query.OrderBy(i => i.StartLocal).ToList();
        }
    }

    public IReadOnlyList<ScheduleItem> GetForDay(DateTime day)
    {
        var date = day.Date;
        lock (_gate)
        {
            return _items
                .Where(i => i.DeletedAt is null && i.StartLocal.Date == date)
                .OrderBy(i => i.StartLocal)
                .ToList();
        }
    }

    public IReadOnlyList<ScheduleItem> GetUpcoming(int count = 20)
    {
        var now = DateTime.Now;
        lock (_gate)
        {
            return _items
                .Where(i => i.DeletedAt is null && i.StartLocal >= now)
                .OrderBy(i => i.StartLocal)
                .Take(count)
                .ToList();
        }
    }

    public ScheduleItem? Find(string id)
    {
        lock (_gate)
        {
            return _items.FirstOrDefault(i => i.Id == id);
        }
    }

    public void Add(ScheduleItem item)
    {
        lock (_gate)
        {
            _items.Add(item);
        }

        Save();
        RaiseChanged();
    }

    public void Update(ScheduleItem item)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == item.Id);
            if (index >= 0)
            {
                _items[index] = item;
            }
        }

        Save();
        RaiseChanged();
    }

    public void MarkDeleted(string id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item is null)
            {
                return;
            }

            item.DeletedAt = DateTime.Now;
        }

        Save();
        RaiseChanged();
    }

    public void RemoveHard(string id)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => i.Id == id);
        }

        Save();
        RaiseChanged();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_items, JsonOptions);
            }

            var temp = AppPaths.SchedulesFilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.SchedulesFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存 schedules.json 失败", ex);
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("日程变更事件处理异常", ex);
        }
    }
}
