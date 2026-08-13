using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed class PowerSessionStore
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly DateTimeOffset _currentStartUtc;
    private readonly List<SessionDto> _sessions = [];

    public PowerSessionStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "power_sessions.json");
        _currentStartUtc = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        Load();
        RecordHeartbeat();
    }

    public IReadOnlyList<Interval> GetIntervals()
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return _sessions
                .Select(session => new Interval(session.StartUtc, session.EndUtc))
                .Append(new Interval(_currentStartUtc, DateTimeOffset.MaxValue))
                .Where(interval => interval.End > interval.Start && interval.Start <= now)
                .ToList();
        }
    }

    public void RecordHeartbeat()
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SessionDto? current = _sessions.FirstOrDefault(session =>
                Math.Abs((session.StartUtc - _currentStartUtc).TotalSeconds) < 5);
            if (current is null)
            {
                current = new SessionDto { StartUtc = _currentStartUtc };
                _sessions.Add(current);
            }
            current.EndUtc = now;
            DateTimeOffset cutoff = now.AddDays(-90);
            _sessions.RemoveAll(session => session.EndUtc < cutoff);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new FileData
                {
                    Sessions = _sessions.OrderBy(session => session.StartUtc).ToList(),
                }));
            }
            catch (Exception ex)
            {
                AppLog.Info($"电源会话心跳保存失败：{ex.Message}");
            }
        }
    }

    private void Load()
    {
        try
        {
            if (AtomicFile.TryDeserialize(_filePath, out FileData? data) && data?.Sessions is not null)
            {
                _sessions.AddRange(data.Sessions.Where(session => session.EndUtc > session.StartUtc));
            }
        }
        catch (Exception ex)
        {
            AppLog.Info($"电源会话记录加载失败：{ex.Message}");
        }
    }

    private sealed class FileData
    {
        [JsonPropertyName("sessions")]
        public List<SessionDto> Sessions { get; set; } = [];
    }

    private sealed class SessionDto
    {
        [JsonPropertyName("start_utc")]
        public DateTimeOffset StartUtc { get; set; }

        [JsonPropertyName("end_utc")]
        public DateTimeOffset EndUtc { get; set; }
    }
}
