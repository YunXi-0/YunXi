using Microsoft.Win32;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace PcCompanionMonitor;

internal sealed record StatsSnapshot(TimeSpan Powered, TimeSpan Awake, TimeSpan Active, bool Ready);

internal sealed record AllTimeSummary(
    TimeSpan TotalActive,
    long TotalMouse,
    long TotalLeft,
    long TotalRight,
    long TotalKeyboard,
    long TotalWasd,
    long TotalQwer,
    long TotalShift,
    long TotalCtrl,
    long TotalTab,
    TimeSpan TotalQqActive,
    TimeSpan TotalWeChatActive,
    TimeSpan TotalMouseIdle,
    double AverageCps,
    double AverageKps,
    double AverageAps);

internal sealed class MonitorEngine : IDisposable
{
    private readonly ActivityStore _store;
    private readonly DailyDataStore _daily;
    private readonly InputUsageStore _inputStore;
    private readonly InputUsageCounter _inputCounter;
    private readonly PowerSessionStore _powerSessions;
    private readonly Timer _timer;
    private readonly ForegroundAppUsageTracker _appUsageTracker = new();

    private PowerEventHistory _power = new([], []);
    private DateTimeOffset _bucketStart;
    private DateTimeOffset _bucketEnd;
    private DateTimeOffset _lastTickUtc;
    private DateTime _savedDay = DateTime.Now.Date;
    private DateTime _appUsageDate = DateTime.Now.Date;
    private long _qqActiveSeconds;
    private long _weChatActiveSeconds;
    private DateTime _mouseIdleDate = DateTime.Now.Date;
    private long _mouseIdleSeconds;
    private bool _systemSuspended;
    private bool _workstationLocked;
    private DateTimeOffset _lastDailySaveUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastPowerHeartbeatUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastPowerLoadAttemptUtc;
    private bool _bucketInput;
    private bool _powerReady;
    private bool _powerReloadPending = true;
    private bool _disposed;
    private SynchronizationContext? _uiContext;
    private int _powerReloading;

    public MonitorEngine(ActivityStore store)
    {
        _store = store;
        _daily = new DailyDataStore();
        _inputStore = new InputUsageStore(_daily.DataDirectory);
        _inputCounter = new InputUsageCounter(_inputStore);
        _powerSessions = new PowerSessionStore(_daily.DataDirectory);
        _bucketStart = Truncate(DateTimeOffset.UtcNow);
        _bucketEnd = _bucketStart.AddSeconds(5);
        _timer = new Timer { Interval = 1000 };
        _timer.Tick += OnTick;
    }

    public string DataDirectory => _daily.DataDirectory;

    public event EventHandler<StatsSnapshot>? StatsChanged;

    public void Start()
    {
        _uiContext = SynchronizationContext.Current;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _inputCounter.Start();
        _timer.Start();
        _ = ReloadPowerHistoryAsync(TimeSpan.Zero);
    }

    public StatsSnapshot GetSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset start = now.AddDays(-1);
        MonitorStats stats = _power.GetStats(start, now);
        return new StatsSnapshot(stats.Powered, stats.Awake, TimeSpan.FromSeconds(_store.GetActiveSeconds(start, now)), _powerReady);
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
        return new StatsSnapshot(stats.Powered, stats.Awake, active, _powerReady);
    }

    public InputCounts GetInputDay(DateTime date)
    {
        return _inputStore.GetDayCounts(date);
    }

    public InputMaxRates GetCurrentInputMax()
    {
        return _inputStore.GetDayMax(DateTime.Now.Date);
    }

    public AllTimeSummary GetAllTimeSummary()
    {
        DateTime today = DateTime.Now.Date;
        IReadOnlyList<DailyRecord> records = _daily.LoadAll();
        long active = 0, mouse = 0, left = 0, right = 0, keyboard = 0;
        long wasd = 0, qwer = 0, shift = 0, ctrl = 0, tab = 0;
        long qqActive = 0, weChatActive = 0, mouseIdle = 0;
        double cps = 0, kps = 0, aps = 0;
        int historicalDays = 0;
        foreach (DailyRecord r in records)
        {
            if (r.Date.Date >= today)
            {
                continue;
            }

            active += (long)r.Active.TotalSeconds;
            left += r.MouseLeft;
            right += r.MouseRight;
            keyboard += r.Keyboard;
            wasd += r.Wasd;
            qwer += r.Qwer;
            shift += r.Shift;
            ctrl += r.Ctrl;
            tab += r.Tab;
            qqActive += r.QqActiveSeconds;
            weChatActive += r.WeChatActiveSeconds;
            mouseIdle += r.MouseIdleSeconds;
            cps += r.MaxCps;
            kps += r.MaxKps;
            aps += r.MaxAps;
            historicalDays++;
        }
        // ?????????
        InputCounts todayCounts = _inputStore.GetDayCounts(today);
        left += todayCounts.Left;
        right += todayCounts.Right;
        keyboard += todayCounts.Keyboard;
        wasd += todayCounts.Wasd;
        qwer += todayCounts.Qwer;
        shift += todayCounts.Shift;
        ctrl += todayCounts.Ctrl;
        tab += todayCounts.Tab;
        (long todayQqActive, long todayWeChatActive) = GetAppUsageSeconds(today);
        qqActive += todayQqActive;
        weChatActive += todayWeChatActive;
        mouseIdle += GetMouseIdleSeconds(today);
        mouse = left + right;
        active += (long)GetDaySnapshot(today).Active.TotalSeconds;

        InputMaxRates todayMax = _inputStore.GetDayMax(today);
        cps += todayMax.Cps;
        kps += todayMax.Kps;
        aps += todayMax.Aps;

        int days = historicalDays + 1;
        return new AllTimeSummary(
            TimeSpan.FromSeconds(active),
            mouse,
            left,
            right,
            keyboard,
            wasd,
            qwer,
            shift,
            ctrl,
            tab,
            TimeSpan.FromSeconds(qqActive),
            TimeSpan.FromSeconds(weChatActive),
            TimeSpan.FromSeconds(mouseIdle),
            cps / days,
            kps / days,
            aps / days);
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
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _powerSessions.RecordHeartbeat();
        SaveDaily(DateTime.Now.Date);
        _inputCounter.Dispose();
        _inputStore.SaveIfDirty();
        _store.SaveIfDirty();
        _timer.Stop();
        _timer.Dispose();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.Mode == PowerModes.Suspend)
        {
            _systemSuspended = true;
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _systemSuspended = false;
            _powerReloadPending = true;
            _ = ReloadPowerHistoryAsync(TimeSpan.FromSeconds(5));
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _workstationLocked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionUnlock => false,
            SessionSwitchReason.SessionLogon => false,
            SessionSwitchReason.RemoteConnect => false,
            _ => _workstationLocked,
        };
    }

    private static bool IsScreenSaverRunning()
    {
        return SystemParametersInfo(0x0072, 0, out bool running, 0) && running;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    private async Task ReloadPowerHistoryAsync(TimeSpan delay)
    {
        if (Interlocked.Exchange(ref _powerReloading, 1) != 0)
        {
            return;
        }

        try
        {
            _lastPowerLoadAttemptUtc = DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }
            PowerEventHistory history = await Task.Run(
                () => PowerEventHistory.Load(_powerSessions.GetIntervals())).ConfigureAwait(false);
            SynchronizationContext? context = _uiContext;
            if (_disposed || context is null)
            {
                return;
            }

            context.Post(_ =>
            {
                if (_disposed)
                {
                    return;
                }

                _power = history;
                _powerReady = true;
                _powerReloadPending = false;
                SaveDaily(DateTime.Now.Date);
                Raise();
            }, null);
        }
        catch (Exception ex)
        {
            AppLog.Info($"电源统计加载失败，将稍后重试：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _powerReloading, 0);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastPowerHeartbeatUtc >= TimeSpan.FromMinutes(1))
        {
            _powerSessions.RecordHeartbeat();
            _lastPowerHeartbeatUtc = now;
        }
        if (_powerReloadPending &&
            now - _lastPowerLoadAttemptUtc >= TimeSpan.FromMinutes(1) &&
            Volatile.Read(ref _powerReloading) == 0)
        {
            _ = ReloadPowerHistoryAsync(TimeSpan.Zero);
        }

        DateTime localToday = DateTime.Now.Date;
        if (localToday != _savedDay)
        {
            SaveDaily(_savedDay);
            _savedDay = localToday;
            ResetAppUsage(localToday);
            ResetMouseIdle(localToday);
            _lastDailySaveUtc = now;
        }
        else if (now - _lastDailySaveUtc >= TimeSpan.FromMinutes(5))
        {
            SaveDaily(localToday);
            _lastDailySaveUtc = now;
        }

        UpdateAppUsage(localToday);
        UpdateMouseIdle(localToday);

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

    private void ResetAppUsage(DateTime date)
    {
        _appUsageDate = date;
        _qqActiveSeconds = 0;
        _weChatActiveSeconds = 0;
    }

    private void UpdateAppUsage(DateTime date)
    {
        if (_systemSuspended)
        {
            return;
        }

        if (date != _appUsageDate)
        {
            ResetAppUsage(date);
        }

        AppUsageSnapshot sample = _appUsageTracker.Sample();
        if (sample.QqActive)
        {
            _qqActiveSeconds++;
        }
        if (sample.WeChatActive)
        {
            _weChatActiveSeconds++;
        }
    }

    private (long QqActiveSeconds, long WeChatActiveSeconds) GetAppUsageSeconds(DateTime date)
    {
        return date == _appUsageDate
            ? (_qqActiveSeconds, _weChatActiveSeconds)
            : (0, 0);
    }

    private void ResetMouseIdle(DateTime date)
    {
        _mouseIdleDate = date;
        _mouseIdleSeconds = 0;
    }

    private void UpdateMouseIdle(DateTime date)
    {
        if (_systemSuspended || _workstationLocked || IsScreenSaverRunning())
        {
            return;
        }

        if (date != _mouseIdleDate)
        {
            ResetMouseIdle(date);
        }

        if (Environment.TickCount64 - _inputCounter.LastMouseMoveTick > 5_000)
        {
            _mouseIdleSeconds++;
        }
    }

    private long GetMouseIdleSeconds(DateTime date)
    {
        return date == _mouseIdleDate ? _mouseIdleSeconds : 0;
    }

    private void SaveDaily(DateTime date)
    {
        if (!_powerReady)
        {
            AppLog.Info($"跳过 {date:yyyy-MM-dd} 每日保存：电源统计尚未就绪");
            return;
        }

        DateTimeOffset start = new(date.Year, date.Month, date.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(date));
        DateTimeOffset end = start.AddDays(1);
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset windowEnd = now < end ? now.ToUniversalTime() : end.ToUniversalTime();
        MonitorStats stats = _power.GetStats(start.ToUniversalTime(), windowEnd);
        InputCounts input = _inputStore.GetDayCounts(date);
        InputMaxRates max = _inputStore.GetDayMax(date);
        (long qqActive, long weChatActive) = GetAppUsageSeconds(date);
        long mouseIdle = GetMouseIdleSeconds(date);
        try
        {
            _daily.Save(
                date,
                stats.Powered,
                stats.Awake,
                TimeSpan.FromSeconds(_store.GetActiveSeconds(start.ToUniversalTime(), windowEnd)),
                input.Left,
                input.Right,
                input.Keyboard,
                input.Wasd,
                input.Qwer,
                input.Shift,
                input.Ctrl,
                input.Tab,
                qqActive,
                weChatActive,
                mouseIdle,
                max.Cps,
                max.Kps,
                max.Aps);
        }
        catch (Exception ex)
        {
            AppLog.Info($"每日数据保存失败（{date:yyyy-MM-dd}）：{ex.Message}");
        }
    }

    private void Raise() => StatsChanged?.Invoke(this, GetSnapshot());

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        long ticks = value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerSecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
