using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed class ActivityStore
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly List<Interval> _intervals = [];
    private bool _dirty;

    public ActivityStore(string? dataDirectory = null)
    {
        string directory = dataDirectory ?? DailyDataStore.GetDefaultDirectory();
        _filePath = Path.Combine(directory, "activity.json");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string legacyPath = Path.Combine(local, "PcCompanionMonitor", "activity.json");
        if (!Load(_filePath) && !Path.GetFullPath(legacyPath).Equals(
                Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase) &&
            Load(legacyPath))
        {
            _dirty = true;
        }
    }

    public void AddInterval(Interval interval)
    {
        lock (_lock)
        {
            if (AddOrMergeInterval(interval))
            {
                _dirty = true;
            }
            Prune();
        }
    }

    public long GetActiveSeconds(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        lock (_lock)
        {
            long total = 0;
            foreach (Interval interval in _intervals)
            {
                DateTimeOffset start = interval.Start < startUtc ? startUtc : interval.Start;
                DateTimeOffset end = interval.End > endUtc ? endUtc : interval.End;
                if (end > start)
                {
                    total += (long)(end - start).TotalSeconds;
                }
            }
            return total;
        }
    }

    public void SaveIfDirty()
    {
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }

            Prune();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new FileData
                {
                    Intervals = _intervals.Select(i => new IntervalDto
                    {
                        Start = i.Start.UtcDateTime,
                        End = i.End.UtcDateTime,
                    }).ToList(),
                }));
                _dirty = false;
            }
            catch
            {
            }
        }
    }

    private void Prune()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-26);
        _intervals.RemoveAll(i => i.End < cutoff);
    }

    private bool AddOrMergeInterval(Interval interval)
    {
        if (interval.End <= interval.Start)
        {
            return false;
        }

        if (_intervals.Count == 0 || interval.Start > _intervals[^1].End)
        {
            _intervals.Add(interval);
            return true;
        }

        Interval previous = _intervals[^1];
        if (interval.End <= previous.End)
        {
            return false;
        }

        _intervals[^1] = previous with { End = interval.End };
        return true;
    }

    private bool Load(string path)
    {
        try
        {
            if (AtomicFile.TryDeserialize(path, out FileData? data))
            {
                if (data?.Intervals is not null)
                {
                    int serializedCount = data.Intervals.Count;
                    foreach (IntervalDto dto in data.Intervals.OrderBy(dto => dto.Start))
                    {
                        AddOrMergeInterval(new Interval(
                            new DateTimeOffset(DateTime.SpecifyKind(dto.Start, DateTimeKind.Utc)),
                            new DateTimeOffset(DateTime.SpecifyKind(dto.End, DateTimeKind.Utc))));
                    }
                    if (_intervals.Count != serializedCount)
                    {
                        _dirty = true;
                    }
                }
                Prune();
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private sealed class FileData
    {
        public List<IntervalDto> Intervals { get; set; } = [];
    }

    private sealed class IntervalDto
    {
        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("end")]
        public DateTime End { get; set; }
    }
}
