using Timer = System.Windows.Forms.Timer;

namespace PcCompanionMonitor;

internal sealed record StatsSnapshot(TimeSpan Powered, TimeSpan Awake, TimeSpan Active, bool Ready);

internal sealed class MonitorEngine : IDisposable
{
    private readonly ActivityStore _store;
    private readonly DailyDataStore _daily;
    private readonly InputUsageStore _inputStore;
    private readonly InputUsageCounter _inputCounter;
    private readonly Timer _timer;

    private PowerEventHistory _power = new([], []);
    private DateTimeOffset _bucketStart;
    private DateTimeOffset _bucketEnd;
    private DateTimeOffset _lastTickUtc;
    private DateTime _savedDay = DateTime.Now.Date;
    private DateTimeOffset _lastDailySaveUtc = DateTimeOffset.UtcNow;
    private bool _bucketInput;
    private bool _disposed;

    public MonitorEngine(ActivityStore store)
    {
        _store = store;
        _daily = new DailyDataStore();
        _inputStore = new InputUsageStore(_daily.DataDirectory);
        _inputCounter = new InputUsageCounter(_inputStore);
        _bucketStart = Truncate(DateTimeOffset.UtcNow);
        _bucketEnd = _bucketStart.AddSeconds(5);
        _timer = new Timer { Interval = 1000 };
        _timer.Tick += OnTick;
    }

    public string DataDirectory => _daily.DataDirectory;

    public event EventHandler<StatsSnapshot>? StatsChanged;

    public void Start()
    {
        _inputCounter.Start();
        _timer.Start();
        _ = Task.Run(() =>
        {
            try { return PowerEventHistory.Load(); }
            catch { return new PowerEventHistory([], []); }
        }).ContinueWith(t =>
        {
            if (!_disposed)
            {
                _power = t.IsCompletedSuccessfully ? t.Result : new PowerEventHistory([], []);
                SaveDaily(DateTime.Now.Date);
                Raise();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public StatsSnapshot GetSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset start = now.AddDays(-1);
        MonitorStats stats = _power.GetStats(start, now);
        return new StatsSnapshot(stats.Powered, stats.Awake, TimeSpan.FromSeconds(_store.GetActiveSeconds(start, now)), true);
    }

    public StatsSnapshot GetDaySnapshot(DateTime date)
    {
        DateTimeOffset start = new(
            date.Year,
            date.Month,
            date.Day,
            0,
            0,
            0,
            TimeZoneInfo.Local.GetUtcOffset(date));
        DateTimeOffset end = start.AddDays(1);
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset windowEnd = now < end ? now.ToUniversalTime() : end.ToUniversalTime();
        MonitorStats stats = _power.GetStats(start.ToUniversalTime(), windowEnd);
        TimeSpan active = TimeSpan.FromSeconds(_store.GetActiveSeconds(start.ToUniversalTime(), windowEnd));
        return new StatsSnapshot(stats.Powered, stats.Awake, active, true);
    }

    public InputCounts GetInputDay(DateTime date)
    {
        return _inputStore.GetDayCounts(date);
    }

    public InputMaxRates GetCurrentInputMax()
    {
        return _inputStore.GetDayMax(DateTime.Now.Date);
    }

    public IReadOnlyDictionary<string, double> GetDailyLeaderboardValues(DateTime date)
    {
        DateTimeOffset start = new(
            date.Year,
            date.Month,
            date.Day,
            0,
            0,
            0,
            TimeZoneInfo.Local.GetUtcOffset(date));
        DateTimeOffset end = start.AddDays(1);
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset windowEnd = now < end ? now.ToUniversalTime() : end.ToUniversalTime();
        MonitorStats stats = _power.GetStats(start.ToUniversalTime(), windowEnd);
        TimeSpan active = TimeSpan.FromSeconds(_store.GetActiveSeconds(start.ToUniversalTime(), windowEnd));
        InputCounts input = _inputStore.GetDayCounts(date);

        return new Dictionary<string, double>
        {
            ["active"] = active.TotalSeconds,
            ["mouse_total"] = input.Total,
            ["mouse_left"] = input.Left,
            ["mouse_right"] = input.Right,
            ["keyboard"] = input.Keyboard,
        };
    }

    public IReadOnlyDictionary<string, double> GetDailyLeaderboardValues7Day()
    {
        IReadOnlyList<DailyStatsPoint> points = GetDailyStats(7);
        return new Dictionary<string, double>
        {
            ["active7"] = points.Sum(p => p.Active.TotalSeconds),
            ["mouse_total7"] = points.Sum(p => p.MouseTotal),
            ["mouse_left7"] = points.Sum(p => p.MouseLeft),
            ["mouse_right7"] = points.Sum(p => p.MouseRight),
            ["keyboard7"] = points.Sum(p => p.Keyboard),
        };
    }

    public IReadOnlyDictionary<string, double> GetDailyLeaderboardValues30Day()
    {
        IReadOnlyList<DailyStatsPoint> points = GetDailyStats(30);
        return new Dictionary<string, double>
        {
            ["active30"] = points.Sum(p => p.Active.TotalSeconds),
            ["mouse_total30"] = points.Sum(p => p.MouseTotal),
            ["mouse_left30"] = points.Sum(p => p.MouseLeft),
            ["mouse_right30"] = points.Sum(p => p.MouseRight),
            ["keyboard30"] = points.Sum(p => p.Keyboard),
        };
    }

    public IReadOnlyDictionary<string, double> GetDailyLeaderboardValuesAllTime()
    {
        DateTime todayAllTime = DateTime.Now.Date;
        IReadOnlyList<DailyRecord> allRecords = _daily.LoadAll()
            .Where(record => record.Date.Date < todayAllTime)
            .ToList();
        InputCounts todayInput = _inputStore.GetDayCounts(DateTime.Now.Date);
        StatsSnapshot todayStats = GetDaySnapshot(DateTime.Now.Date);
        long activeTotal = allRecords.Sum(r => (long)r.Active.TotalSeconds) + (long)todayStats.Active.TotalSeconds;
        long mouseTotal = allRecords.Sum(r => r.MouseTotal) + todayInput.Total;
        long mouseLeft = allRecords.Sum(r => r.MouseLeft) + todayInput.Left;
        long mouseRight = allRecords.Sum(r => r.MouseRight) + todayInput.Right;
        long keyboard = allRecords.Sum(r => r.Keyboard) + todayInput.Keyboard;
        return new Dictionary<string, double>
        {
            ["active_total"] = activeTotal,
            ["mouse_total_total"] = mouseTotal,
            ["mouse_left_total"] = mouseLeft,
            ["mouse_right_total"] = mouseRight,
            ["keyboard_total"] = keyboard,
        };
    }

    public IReadOnlyList<DailyStatsPoint> GetDailyStats(int days)
    {
        var points = new List<DailyStatsPoint>();
        DateTime today = DateTime.Now.Date;
        for (int i = days - 1; i >= 0; i--)
        {
            DateTime date = today.AddDays(-i);
            if (date < today)
            {
                DailyRecord? saved = _daily.Load(date);
                if (saved is not null)
                {
                    points.Add(new DailyStatsPoint(
                        date,
                        saved.Powered,
                        saved.Awake,
                        saved.Active,
                        saved.MouseTotal,
                        saved.MouseLeft,
                        saved.MouseRight,
                        saved.Keyboard));
                    continue;
                }
            }

            // Skip past days without saved data to avoid zero-dilution in cumulative sums
            if (date < today) continue;
            DateTimeOffset start = new(date.Year, date.Month, date.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(date));
            DateTimeOffset end = start.AddDays(1);
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset windowEnd = now < end ? now.ToUniversalTime() : end.ToUniversalTime();
            MonitorStats stats = _power.GetStats(start.ToUniversalTime(), windowEnd);
            InputCounts input = _inputStore.GetDayCounts(date);
            points.Add(new DailyStatsPoint(
                date,
                stats.Powered,
                stats.Awake,
                TimeSpan.FromSeconds(_store.GetActiveSeconds(start.ToUniversalTime(), windowEnd)),
                input.Total,
                input.Left,
                input.Right,
                input.Keyboard));
        }
        return points;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveDaily(DateTime.Now.Date);
        _inputCounter.Dispose();
        _inputStore.SaveIfDirty();
        _store.SaveIfDirty();
        _timer.Stop();
        _timer.Dispose();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTime localToday = DateTime.Now.Date;
        if (localToday != _savedDay)
        {
            SaveDaily(_savedDay);
            _savedDay = localToday;
            _lastDailySaveUtc = now;
        }
        else if (now - _lastDailySaveUtc >= TimeSpan.FromMinutes(5))
        {
            SaveDaily(localToday);
            _lastDailySaveUtc = now;
        }

        bool longGap = _lastTickUtc != default && now - _lastTickUtc > TimeSpan.FromSeconds(2);
        _lastTickUtc = now;

        if (longGap)
        {
            ResetBucket(now);
        }
        else if (now >= _bucketEnd)
        {
            if (_bucketInput)
            {
                _store.AddInterval(new Interval(_bucketStart, _bucketEnd));
            }
            _bucketStart = _bucketEnd;
            _bucketEnd = _bucketStart.AddSeconds(5);
            _bucketInput = false;
        }

        if (InputDetector.HasRecentInput(TimeSpan.FromSeconds(5)))
        {
            _bucketInput = true;
        }

        _store.SaveIfDirty();
        _inputStore.SaveIfDirty();
        Raise();
    }

    private void ResetBucket(DateTimeOffset now)
    {
        _bucketStart = Truncate(now);
        _bucketEnd = _bucketStart.AddSeconds(5);
        _bucketInput = false;
    }

    private void SaveDaily(DateTime date)
    {
        DateTimeOffset start = new(date.Year, date.Month, date.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(date));
        DateTimeOffset end = start.AddDays(1);
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset windowEnd = now < end ? now.ToUniversalTime() : end.ToUniversalTime();
        MonitorStats stats = _power.GetStats(start.ToUniversalTime(), windowEnd);
        InputCounts input = _inputStore.GetDayCounts(date);
        InputMaxRates max = _inputStore.GetDayMax(date);
        _daily.Save(
            date,
            stats.Powered,
            stats.Awake,
            TimeSpan.FromSeconds(_store.GetActiveSeconds(start.ToUniversalTime(), windowEnd)),
            input.Left,
            input.Right,
            input.Keyboard,
            max.Cps,
            max.Kps,
            max.Aps);
    }

    private void Raise() => StatsChanged?.Invoke(this, GetSnapshot());

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        long ticks = value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
