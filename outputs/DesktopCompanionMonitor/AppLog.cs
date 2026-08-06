namespace PcCompanionMonitor;

internal static class AppLog
{
    public static void Info(string message)
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "log");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                $"CloudXiPCStatistics_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
