using System.Text.Json;

namespace PcCompanionMonitor;

internal static class LeaderboardSettingsStore
{
    private static string FilePath => Path.Combine(new DailyDataStore().DataDirectory, "settings.json");

    public static string DefaultUserId()
    {
        string id = Sanitize(Environment.UserName);
        return string.IsNullOrEmpty(id) ? "USER" : id;
    }

    public static string LoadUserId()
    {
        string id = LoadSettings().UserId;
        return string.IsNullOrEmpty(id) ? DefaultUserId() : id;
    }

    public static void SaveUserId(string userId)
    {
        try
        {
            SettingsFile data = LoadSettings();
            data.UserId = Sanitize(userId);
            SaveSettings(data);
        }
        catch
        {
        }
    }

    public static int? LoadLuckValue(DateTime date)
    {
        SettingsFile data = LoadSettings();
        if (data.LuckDate == date.ToString("yyyy-MM-dd") && data.LuckValue is int value)
        {
            return value;
        }
        return null;
    }

    public static void SaveLuckValue(DateTime date, int value)
    {
        try
        {
            SettingsFile data = LoadSettings();
            data.LuckDate = date.ToString("yyyy-MM-dd");
            data.LuckValue = Math.Clamp(value, 0, 100);
            SaveSettings(data);
        }
        catch
        {
        }
    }

    public static int LoadCollectionCount()
    {
        return Math.Max(0, LoadSettings().CollectionCount ?? 0);
    }

    public static void SaveCollectionCount(int count)
    {
        try
        {
            SettingsFile data = LoadSettings();
            data.CollectionCount = Math.Max(0, count);
            SaveSettings(data);
        }
        catch
        {
        }
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return new string(value.Where(char.IsLetterOrDigit).Take(10).ToArray());
    }

    private static SettingsFile LoadSettings()
    {
        try
        {
            if (AtomicFile.TryDeserialize(FilePath, out SettingsFile? settings))
            {
                return settings ?? new SettingsFile();
            }
        }
        catch
        {
        }
        return new SettingsFile();
    }

    private static void SaveSettings(SettingsFile data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    private sealed class SettingsFile
    {
        public string UserId { get; set; } = "";
        public string LuckDate { get; set; } = "";
        public int? LuckValue { get; set; }
        public int? CollectionCount { get; set; }
    }
}
