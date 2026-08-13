using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

[Flags]
internal enum KeyCategory
{
    None = 0,
    Wasd = 1,
    Qwer = 2,
    Shift = 4,
    Ctrl = 8,
    Tab = 16,
}

internal readonly record struct InputCounts(
    long Left,
    long Right,
    long Keyboard,
    long Wasd = 0,
    long Qwer = 0,
    long Shift = 0,
    long Ctrl = 0,
    long Tab = 0)
{
    public long Total => Left + Right;
}

internal readonly record struct InputMaxRates(double Cps, double Kps, double Aps);

internal sealed class InputUsageStore
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly Dictionary<long, InputCounts> _buckets = [];
    private bool _dirty;
    private long _currentSecond;
    private int _secondClicks;
    private int _secondKeys;
    private readonly Dictionary<DateTime, InputMaxRates> _dailyMaxima = [];

    public InputUsageStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "input_usage.json");
        Directory.CreateDirectory(dataDirectory);
        Load();
    }

    public void AddLeftClick() => Add(1, 0, 0, 0, 0, 0, 0, 0);
    public void AddRightClick() => Add(0, 1, 0, 0, 0, 0, 0, 0);
    public void AddKeyboardPress(KeyCategory categories = KeyCategory.None)
    {
        long wasd = (categories & KeyCategory.Wasd) != 0 ? 1 : 0;
        long qwer = (categories & KeyCategory.Qwer) != 0 ? 1 : 0;
        long shift = (categories & KeyCategory.Shift) != 0 ? 1 : 0;
        long ctrl = (categories & KeyCategory.Ctrl) != 0 ? 1 : 0;
        long tab = (categories & KeyCategory.Tab) != 0 ? 1 : 0;
        Add(0, 0, 1, wasd, qwer, shift, ctrl, tab);
    }

    public InputCounts GetCounts(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        lock (_lock)
        {
            long start = startUtc.ToUnixTimeSeconds() / 60;
            long end = endUtc.ToUnixTimeSeconds() / 60;
            long left = 0, right = 0, key = 0, wasd = 0, qwer = 0, shift = 0, ctrl = 0, tab = 0;
            foreach (KeyValuePair<long, InputCounts> p in _buckets)
            {
                if (p.Key >= start && p.Key < end)
                {
                    left += p.Value.Left;
                    right += p.Value.Right;
                    key += p.Value.Keyboard;
                    wasd += p.Value.Wasd;
                    qwer += p.Value.Qwer;
                    shift += p.Value.Shift;
                    ctrl += p.Value.Ctrl;
                    tab += p.Value.Tab;
                }
            }
            return new InputCounts(left, right, key, wasd, qwer, shift, ctrl, tab);
        }
    }

    public InputCounts GetDayCounts(DateTime date)
    {
        DateTimeOffset start = new(date.Year, date.Month, date.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(date));
        return GetCounts(start.ToUniversalTime(), start.AddDays(1).ToUniversalTime());
    }

    public InputMaxRates GetDayMax(DateTime date)
    {
        lock (_lock)
        {
            InputMaxRates maximum = _dailyMaxima.GetValueOrDefault(date.Date);
            if (_currentSecond != 0 && DateForSecond(_currentSecond) == date.Date)
            {
                maximum = Max(
                    maximum,
                    new InputMaxRates(_secondClicks, _secondKeys, _secondClicks + _secondKeys));
            }
            return maximum;
        }
    }

    public void SaveIfDirty()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            Prune();
            try
            {
                Dictionary<DateTime, InputMaxRates> maxima = CreateMaximaSnapshot();
                InputMaxRates todayMaximum = maxima.GetValueOrDefault(DateTime.Now.Date);
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new FileData
                {
                    MaxDate = DateTime.Now.ToString("yyyy-MM-dd"),
                    MaxCps = todayMaximum.Cps,
                    MaxKps = todayMaximum.Kps,
                    MaxAps = todayMaximum.Aps,
                    Maxima = maxima
                        .OrderBy(pair => pair.Key)
                        .Select(pair => new DailyMaxDto
                        {
                            Date = pair.Key.ToString("yyyy-MM-dd"),
                            Cps = pair.Value.Cps,
                            Kps = pair.Value.Kps,
                            Aps = pair.Value.Aps,
                        })
                        .ToList(),
                    CurrentSecond = _currentSecond,
                    SecondClicks = _secondClicks,
                    SecondKeys = _secondKeys,
                    Buckets = _buckets.Select(p => new BucketDto
                    {
                        Minute = p.Key,
                        Left = p.Value.Left,
                        Right = p.Value.Right,
                        Keyboard = p.Value.Keyboard,
                        Wasd = p.Value.Wasd,
                        Qwer = p.Value.Qwer,
                        Shift = p.Value.Shift,
                        Ctrl = p.Value.Ctrl,
                        Tab = p.Value.Tab,
                    }).ToList(),
                }));
                _dirty = false;
            }
            catch
            {
            }
        }
    }

    private void Add(long left, long right, long key, long wasd, long qwer, long shift, long ctrl, long tab)
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long second = now.ToUnixTimeSeconds();
            if (_currentSecond == 0)
            {
                _currentSecond = second;
            }
            else if (second != _currentSecond)
            {
                FinalizeCurrentSecond();
                _currentSecond = second;
                _secondClicks = 0;
                _secondKeys = 0;
            }

            _secondClicks += (int)(left + right);
            _secondKeys += (int)key;

            long minute = second / 60;
            InputCounts current = _buckets.GetValueOrDefault(minute);
            _buckets[minute] = new InputCounts(
                current.Left + left,
                current.Right + right,
                current.Keyboard + key,
                current.Wasd + wasd,
                current.Qwer + qwer,
                current.Shift + shift,
                current.Ctrl + ctrl,
                current.Tab + tab);

            _dirty = true;
            Prune();
        }
    }

    private void FinalizeCurrentSecond()
    {
        if (_currentSecond == 0)
        {
            return;
        }

        UpdateMaximum(DateForSecond(_currentSecond), _secondClicks, _secondKeys);
    }

    private void UpdateMaximum(DateTime date, int clicks, int keys)
    {
        InputMaxRates current = _dailyMaxima.GetValueOrDefault(date.Date);
        _dailyMaxima[date.Date] = Max(
            current,
            new InputMaxRates(clicks, keys, clicks + keys));
    }

    private Dictionary<DateTime, InputMaxRates> CreateMaximaSnapshot()
    {
        Dictionary<DateTime, InputMaxRates> snapshot = new(_dailyMaxima);
        if (_currentSecond != 0)
        {
            DateTime date = DateForSecond(_currentSecond);
            snapshot[date] = Max(
                snapshot.GetValueOrDefault(date),
                new InputMaxRates(_secondClicks, _secondKeys, _secondClicks + _secondKeys));
        }
        return snapshot;
    }

    private void Prune()
    {
        long cutoff = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds() / 60;
        foreach (long key in _buckets.Keys.Where(k => k < cutoff).ToList()) _buckets.Remove(key);
        DateTime oldestMaximum = DateTime.Now.Date.AddDays(-7);
        foreach (DateTime date in _dailyMaxima.Keys.Where(date => date < oldestMaximum).ToList())
        {
            _dailyMaxima.Remove(date);
        }
    }

    private void Load()
    {
        try
        {
            if (AtomicFile.TryDeserialize(_filePath, out FileData? data))
            {
                if (data?.Buckets is not null)
                {
                    foreach (BucketDto b in data.Buckets)
                    {
                        _buckets[b.Minute] = new InputCounts(
                            b.Left,
                            b.Right,
                            b.Keyboard,
                            b.Wasd,
                            b.Qwer,
                            b.Shift,
                            b.Ctrl,
                            b.Tab);
                    }
                }

                if (data?.Maxima is not null)
                {
                    foreach (DailyMaxDto maximum in data.Maxima)
                    {
                        if (TryParseDate(maximum.Date, out DateTime date))
                        {
                            _dailyMaxima[date] = Max(
                                _dailyMaxima.GetValueOrDefault(date),
                                new InputMaxRates(maximum.Cps, maximum.Kps, maximum.Aps));
                        }
                    }
                }

                if (data is not null && TryParseDate(data.MaxDate, out DateTime maxDate))
                {
                    _dailyMaxima[maxDate] = Max(
                        _dailyMaxima.GetValueOrDefault(maxDate),
                        new InputMaxRates(data.MaxCps, data.MaxKps, data.MaxAps));
                }

                if (data is not null && data.CurrentSecond > 0)
                {
                    DateTime savedDate = DateForSecond(data.CurrentSecond);
                    UpdateMaximum(savedDate, data.SecondClicks, data.SecondKeys);
                    if (data.CurrentSecond == DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        _currentSecond = data.CurrentSecond;
                        _secondClicks = data.SecondClicks;
                        _secondKeys = data.SecondKeys;
                    }
                }

                Prune();
            }
        }
        catch
        {
        }
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        return DateTime.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out date);
    }

    private static DateTime DateForSecond(long second) =>
        DateTimeOffset.FromUnixTimeSeconds(second).ToLocalTime().Date;

    private static InputMaxRates Max(InputMaxRates left, InputMaxRates right) => new(
        Math.Max(left.Cps, right.Cps),
        Math.Max(left.Kps, right.Kps),
        Math.Max(left.Aps, right.Aps));

    private sealed class FileData
    {
        [JsonPropertyName("max_date")] public string MaxDate { get; set; } = "";
        [JsonPropertyName("max_cps")] public double MaxCps { get; set; }
        [JsonPropertyName("max_kps")] public double MaxKps { get; set; }
        [JsonPropertyName("max_aps")] public double MaxAps { get; set; }
        [JsonPropertyName("maxima")] public List<DailyMaxDto> Maxima { get; set; } = [];
        [JsonPropertyName("current_second")] public long CurrentSecond { get; set; }
        [JsonPropertyName("second_clicks")] public int SecondClicks { get; set; }
        [JsonPropertyName("second_keys")] public int SecondKeys { get; set; }
        public List<BucketDto> Buckets { get; set; } = [];
    }

    private sealed class DailyMaxDto
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("cps")] public double Cps { get; set; }
        [JsonPropertyName("kps")] public double Kps { get; set; }
        [JsonPropertyName("aps")] public double Aps { get; set; }
    }

    private sealed class BucketDto
    {
        [JsonPropertyName("minute")] public long Minute { get; set; }
        [JsonPropertyName("left")] public long Left { get; set; }
        [JsonPropertyName("right")] public long Right { get; set; }
        [JsonPropertyName("keyboard")] public long Keyboard { get; set; }
        [JsonPropertyName("wasd")] public long Wasd { get; set; }
        [JsonPropertyName("qwer")] public long Qwer { get; set; }
        [JsonPropertyName("shift")] public long Shift { get; set; }
        [JsonPropertyName("ctrl")] public long Ctrl { get; set; }
        [JsonPropertyName("tab")] public long Tab { get; set; }
    }
}
