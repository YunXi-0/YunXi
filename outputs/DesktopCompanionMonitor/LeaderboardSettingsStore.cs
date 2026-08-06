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
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                if (doc.RootElement.TryGetProperty("userId", out JsonElement value))
                {
                    return Sanitize(value.GetString() ?? "");
                }
            }
        }
        catch
        {
        }

        return DefaultUserId();
    }

    public static void SaveUserId(string userId)
    {
        try
        {
            string id = Sanitize(userId);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new { userId = id }));
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
}
