namespace PcCompanionMonitor;

internal static class AppLog
{
    private const int MaxFiles = 3;
    private static readonly object Sync = new();
    private static string? _currentPath;

    public static void Initialize()
    {
        lock (Sync)
        {
            try
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(directory);

                string[] existing = Directory.GetFiles(directory, "CloudXiPCStatistics_*.log");
                Array.Sort(existing, (a, b) => File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b)));
                int removeCount = existing.Length - (MaxFiles - 1);
                for (int i = 0; i < removeCount && i < existing.Length; i++)
                {
                    File.Delete(existing[i]);
                }

                _currentPath = Path.Combine(
                    directory,
                    $"CloudXiPCStatistics_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(
                    _currentPath,
                    $"===== 云曦PC统计 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====={Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    public static void Info(string message)
    {
        try
        {
            lock (Sync)
            {
                if (_currentPath is null)
                {
                    Initialize();
                }

                if (_currentPath is not null)
                {
                    File.AppendAllText(
                        _currentPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] {message}{Environment.NewLine}");
                }
            }
        }
        catch
        {
        }
    }
}
