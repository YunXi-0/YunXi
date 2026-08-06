using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed record DailyRecord(
    DateTime Date,
    TimeSpan Powered,
    TimeSpan Awake,
    TimeSpan Active,
    long MouseLeft,
    long MouseRight,
    long Keyboard,
    double MaxCps,
    double MaxKps,
    double MaxAps,
    DateTimeOffset UpdatedAtUtc)
{
    public long MouseTotal => MouseLeft + MouseRight;
}

internal sealed class DailyDataStore
{
    private readonly object _lock = new();
    private readonly string _directory;
    private readonly string _textFile;

    public DailyDataStore(string? directory = null)
    {
        _directory = directory ?? GetDefaultDirectory();
        _textFile = Path.Combine(_directory, "每日数据.txt");
        Directory.CreateDirectory(_directory);
    }

    public string DataDirectory => _directory;

    public void Save(
        DateTime date,
        TimeSpan powered,
        TimeSpan awake,
        TimeSpan active,
        long mouseLeft,
        long mouseRight,
        long keyboard,
        double maxCps,
        double maxKps,
        double maxAps)
    {
        lock (_lock)
        {
            DailyRecord record = new(date, powered, awake, active, mouseLeft, mouseRight, keyboard, maxCps, maxKps, maxAps, DateTimeOffset.UtcNow);
            File.WriteAllText(
                Path.Combine(_directory, $"{date:yyyy-MM-dd}.json"),
                JsonSerializer.Serialize(DailyFile.FromRecord(record)));
            WriteText();
        }
    }

    public DailyRecord? Load(DateTime date)
    {
        lock (_lock)
        {
            string path = Path.Combine(_directory, $"{date:yyyy-MM-dd}.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                DailyFile? dto = JsonSerializer.Deserialize<DailyFile>(File.ReadAllText(path));
                return dto?.ToRecord();
            }
            catch
            {
                return null;
            }
        }
    }

    private void WriteText()
    {
        var records = new List<DailyRecord>();
        foreach (string file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                DailyFile? dto = JsonSerializer.Deserialize<DailyFile>(File.ReadAllText(file));
                if (dto?.ToRecord() is { } r) records.Add(r);
            }
            catch
            {
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("云曦PC统计每日数据");
        sb.AppendLine("==================");
        foreach (DailyRecord r in records.OrderBy(r => r.Date))
        {
            sb.AppendLine($"日期：{r.Date:yyyy-MM-dd}");
            sb.AppendLine($"运行时间：{Format(r.Powered)}（{(long)r.Powered.TotalSeconds} 秒）");
            sb.AppendLine($"非睡眠时间：{Format(r.Awake)}（{(long)r.Awake.TotalSeconds} 秒）");
            sb.AppendLine($"高强度使用：{Format(r.Active)}（{(long)r.Active.TotalSeconds} 秒）");
            sb.AppendLine($"鼠标点击总次数：{r.MouseTotal} 次");
            sb.AppendLine($"鼠标左键点击次数：{r.MouseLeft} 次");
            sb.AppendLine($"鼠标右键点击次数：{r.MouseRight} 次");
            sb.AppendLine($"键盘敲击次数：{r.Keyboard} 次");
            sb.AppendLine($"当日最大CPS：{r.MaxCps:F1} 次/秒");
            sb.AppendLine($"当日最大KPS：{r.MaxKps:F1} 次/秒");
            sb.AppendLine($"当日最大APS：{r.MaxAps:F1} 次/秒");
            sb.AppendLine();
        }
        File.WriteAllText(_textFile, sb.ToString(), new UTF8Encoding(true));
    }

    private static string GetDefaultDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "PCCompanionMonitor.exe")) ||
            File.Exists(Path.Combine(baseDir, "云曦PC统计.exe")) ||
            File.Exists(Path.Combine(baseDir, "云曦PC监测.exe")))
        {
            return Path.Combine(baseDir, "data");
        }
        return Path.Combine(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..")), "data");
    }

    private static string Format(TimeSpan value)
    {
        int total = Math.Max(0, (int)value.TotalSeconds);
        return $"{total / 3600:D2}:{(total % 3600) / 60:D2}:{total % 60:D2}";
    }

    private sealed class DailyFile
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("runtime_seconds")] public long RuntimeSeconds { get; set; }
        [JsonPropertyName("awake_seconds")] public long AwakeSeconds { get; set; }
        [JsonPropertyName("active_seconds")] public long ActiveSeconds { get; set; }
        [JsonPropertyName("mouse_left")] public long MouseLeft { get; set; }
        [JsonPropertyName("mouse_right")] public long MouseRight { get; set; }
        [JsonPropertyName("keyboard")] public long Keyboard { get; set; }
        [JsonPropertyName("max_cps")] public double MaxCps { get; set; }
        [JsonPropertyName("max_kps")] public double MaxKps { get; set; }
        [JsonPropertyName("max_aps")] public double MaxAps { get; set; }
        [JsonPropertyName("updated_at_utc")] public DateTime UpdatedAtUtc { get; set; }

        public static DailyFile FromRecord(DailyRecord r)
        {
            return new DailyFile
            {
                Date = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                RuntimeSeconds = (long)r.Powered.TotalSeconds,
                AwakeSeconds = (long)r.Awake.TotalSeconds,
                ActiveSeconds = (long)r.Active.TotalSeconds,
                MouseLeft = r.MouseLeft,
                MouseRight = r.MouseRight,
                Keyboard = r.Keyboard,
                MaxCps = r.MaxCps,
                MaxKps = r.MaxKps,
                MaxAps = r.MaxAps,
                UpdatedAtUtc = r.UpdatedAtUtc.UtcDateTime,
            };
        }

        public DailyRecord? ToRecord()
        {
            if (!DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return null;
            }
            return new DailyRecord(
                date,
                TimeSpan.FromSeconds(RuntimeSeconds),
                TimeSpan.FromSeconds(AwakeSeconds),
                TimeSpan.FromSeconds(ActiveSeconds),
                MouseLeft,
                MouseRight,
                Keyboard,
                MaxCps,
                MaxKps,
                MaxAps,
                new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)));
        }
    }
}
