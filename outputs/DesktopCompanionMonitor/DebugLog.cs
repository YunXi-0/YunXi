namespace PcCompanionMonitor;

internal static class DebugLog
{
    public static void Write(string message)
    {
        string? path = Environment.GetEnvironmentVariable("PCMONITOR_DEBUG");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
