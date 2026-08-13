using System.Diagnostics.Eventing.Reader;

namespace PcCompanionMonitor;

internal sealed record MonitorStats(TimeSpan Powered, TimeSpan Awake);

internal sealed class PowerEventHistory
{
    private readonly IReadOnlyList<Interval> _powered;
    private readonly IReadOnlyList<Interval> _sleep;

    public PowerEventHistory(IReadOnlyList<Interval> powered, IReadOnlyList<Interval> sleep)
    {
        _powered = powered;
        _sleep = sleep;
    }

    public static PowerEventHistory Load(IReadOnlyList<Interval>? recordedSessions = null)
    {
        var boot = new List<(DateTimeOffset Time, int Id)>();
        var power = new List<(DateTimeOffset Time, int Id)>();
        if (!Read("Microsoft-Windows-Kernel-General", "12,13", boot) ||
            !Read("Microsoft-Windows-Kernel-Power", "42,107,506,507", power))
        {
            throw new InvalidOperationException("系统电源事件日志读取失败");
        }

        List<DateTimeOffset> boots = boot.Where(e => e.Id == 12).Select(e => e.Time).OrderBy(t => t).ToList();
        List<DateTimeOffset> shutdowns = boot.Where(e => e.Id == 13).Select(e => e.Time).OrderBy(t => t).ToList();
        var powered = recordedSessions?.ToList() ?? [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < boots.Count; i++)
        {
            DateTimeOffset bootTime = boots[i];
            DateTimeOffset nextBoot = i + 1 < boots.Count ? boots[i + 1] : DateTimeOffset.MaxValue;
            DateTimeOffset? shutdown = shutdowns.FirstOrDefault(value => value > bootTime && value < nextBoot);
            if (shutdown is { } shutdownTime && shutdownTime > bootTime && shutdownTime < now)
            {
                powered.Add(new Interval(bootTime, shutdownTime));
            }
            else if (i == boots.Count - 1 && bootTime <= now)
            {
                powered.Add(new Interval(bootTime, DateTimeOffset.MaxValue));
            }
        }

        var sleep = new List<Interval>();
        DateTimeOffset? enter = null;
        foreach ((DateTimeOffset Time, int Id) ev in power
                     .Where(e => e.Id is 42 or 107 or 506 or 507)
                     .OrderBy(e => e.Time))
        {
            if (ev.Id is 42 or 506)
            {
                enter ??= ev.Time;
            }
            else if (enter is { } start && ev.Time > start)
            {
                sleep.Add(new Interval(start, ev.Time));
                enter = null;
            }
        }

        return new PowerEventHistory(Merge(powered), sleep);
    }

    public MonitorStats GetStats(DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        long powered = Sum(_powered, windowStart, windowEnd);
        long sleep = 0;
        foreach (Interval s in _sleep)
        {
            foreach (Interval p in _powered)
            {
                DateTimeOffset start = s.Start > p.Start ? s.Start : p.Start;
                DateTimeOffset end = s.End < p.End ? s.End : p.End;
                if (start < windowStart) start = windowStart;
                if (end > windowEnd) end = windowEnd;
                if (end > start) sleep += (long)(end - start).TotalSeconds;
            }
        }
        return new MonitorStats(TimeSpan.FromSeconds(powered), TimeSpan.FromSeconds(Math.Max(0, powered - sleep)));
    }

    private static bool Read(string provider, string ids, List<(DateTimeOffset, int)> output)
    {
        try
        {
            string query = $"*[System[Provider[@Name='{provider}'] and (EventID={string.Join(" or EventID=", ids.Split(','))})]]";
            using EventLogReader reader = new(new EventLogQuery("System", PathType.LogName, query));
            while (true)
            {
                using EventRecord? record = reader.ReadEvent();
                if (record is null) break;
                if (record.TimeCreated is DateTime time)
                {
                    DateTime local = DateTime.SpecifyKind(time, DateTimeKind.Local);
                    output.Add((new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime(), record.Id));
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Info($"系统事件日志读取失败（{provider}）：{ex.Message}");
            return false;
        }
    }

    private static long Sum(IReadOnlyList<Interval> intervals, DateTimeOffset start, DateTimeOffset end)
    {
        long total = 0;
        foreach (Interval interval in intervals)
        {
            DateTimeOffset s = interval.Start < start ? start : interval.Start;
            DateTimeOffset e = interval.End > end ? end : interval.End;
            if (e > s) total += (long)(e - s).TotalSeconds;
        }
        return total;
    }

    private static IReadOnlyList<Interval> Merge(IEnumerable<Interval> source)
    {
        var result = new List<Interval>();
        foreach (Interval interval in source.OrderBy(i => i.Start))
        {
            if (result.Count == 0 || interval.Start > result[^1].End)
            {
                result.Add(interval);
            }
            else if (interval.End > result[^1].End)
            {
                result[^1] = result[^1] with { End = interval.End };
            }
        }
        return result;
    }
}
