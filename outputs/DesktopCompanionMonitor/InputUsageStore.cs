using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal readonly record struct InputCounts(long Left, long Right, long Keyboard)
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
    private long _secondStart;
    private int _secondClicks;
    private int _secondKeys;
    private DateTime _maxDate;
    private double _maxCps;
    private double _maxKps;
    private double _maxAps;

    public InputUsageStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "input_usage.json");
        Directory.CreateDirectory(dataDirectory);
        Load();
    }

    public void AddLeftClick() => Add(1, 0, 0);
    public void AddRightClick() => Add(0, 1, 0);
    public void AddKeyboardPress() => Add(0, 0, 1);

    public InputCounts GetCounts(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        lock (_lock)
        {
            long start = startUtc.ToUnixTimeSeconds() / 60;
            long end = endUtc.ToUnixTimeSeconds() / 60;
            long left = 0, right = 0, key = 0;
            foreach (KeyValuePair<long, InputCounts> p in _buckets)
            {
                if (p.Key >= start && p.Key < end)
                {
                    left += p.Value.Left;
                    right += p.Value.Right;
                    key += p.Value.Keyboard;
                }
            }
            return new InputCounts(left, right, key);
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
            if (date.Date != _maxDate)
            {
                return new InputMaxRates(0, 0, 0);
            }

            double cps = _maxCps;
            double kps = _maxKps;
            double aps = _maxAps;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long second = now.ToUnixTimeSeconds();
            if (_secondClicks > 0 || _secondKeys > 0)
            {
                double elapsed = (second - _secondStart) + now.Millisecond / 1000.0;
                if (elapsed > 0)
                {
                    cps = Math.Max(cps, _secondClicks / elapsed);
                    kps = Math.Max(kps, _secondKeys / elapsed);
                    aps = Math.Max(aps, (_secondClicks + _secondKeys) / elapsed);
                }
            }
            return new InputMaxRates(cps, kps, aps);
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
                File.WriteAllText(_filePath, JsonSerializer.Serialize(new FileData
                {
                    MaxDate = _maxDate.ToString("yyyy-MM-dd"),
                    MaxCps = _maxCps,
                    MaxKps = _maxKps,
                    MaxAps = _maxAps,
                    Buckets = _buckets.Select(p => new BucketDto { Minute = p.Key, Left = p.Value.Left, Right = p.Value.Right, Keyboard = p.Value.Keyboard }).ToList(),
                }));
                _dirty = false;
            }
            catch
            {
            }
        }
    }

    private void Add(long left, long right, long key)
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long second = now.ToUnixTimeSeconds();
            if (_currentSecond == 0)
            {
                _currentSecond = second;
                _secondStart = second;
            }
            else if (second != _currentSecond)
            {
                FlushSecond(_currentSecond, second);
                _currentSecond = second;
                _secondStart = second;
                _secondClicks = 0;
                _secondKeys = 0;
            }

            _secondClicks += (int)(left + right);
            _secondKeys += (int)key;

            long minute = second / 60;
            InputCounts current = _buckets.GetValueOrDefault(minute);
            _buckets[minute] = new InputCounts(current.Left + left, current.Right + right, current.Keyboard + key);

            DateTime today = DateTime.Now.Date;
            if (_maxDate != today)
            {
                _maxDate = today;
                _maxCps = 0;
                _maxKps = 0;
                _maxAps = 0;
            }

            _dirty = true;
            Prune();
        }
    }

    private void FlushSecond(long startSecond, long endSecond)
    {
        long duration = Math.Max(1, endSecond - startSecond);
        double cps = _secondClicks / (double)duration;
        double kps = _secondKeys / (double)duration;
        double aps = (_secondClicks + _secondKeys) / (double)duration;
        _maxCps = Math.Max(_maxCps, cps);
        _maxKps = Math.Max(_maxKps, kps);
        _maxAps = Math.Max(_maxAps, aps);
    }

    private void Prune()
    {
        long cutoff = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds() / 60;
        foreach (long key in _buckets.Keys.Where(k => k < cutoff).ToList()) _buckets.Remove(key);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                FileData? data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(_filePath));
                if (data?.Buckets is not null)
                {
                    foreach (BucketDto b in data.Buckets) _buckets[b.Minute] = new InputCounts(b.Left, b.Right, b.Keyboard);
                }
                if (data is not null && DateTime.TryParseExact(
                        data.MaxDate,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out DateTime maxDate))
                {
                    _maxDate = maxDate;
                    _maxCps = data.MaxCps;
                    _maxKps = data.MaxKps;
                    _maxAps = data.MaxAps;
                }
                Prune();
            }
        }
        catch
        {
        }
    }

    private sealed class FileData
    {
        [JsonPropertyName("max_date")] public string MaxDate { get; set; } = "";
        [JsonPropertyName("max_cps")] public double MaxCps { get; set; }
        [JsonPropertyName("max_kps")] public double MaxKps { get; set; }
        [JsonPropertyName("max_aps")] public double MaxAps { get; set; }
        public List<BucketDto> Buckets { get; set; } = [];
    }
    private sealed class BucketDto
    {
        [JsonPropertyName("minute")] public long Minute { get; set; }
        [JsonPropertyName("left")] public long Left { get; set; }
        [JsonPropertyName("right")] public long Right { get; set; }
        [JsonPropertyName("keyboard")] public long Keyboard { get; set; }
    }
}
