namespace PcCompanionMonitor;

internal static class AppLog
{
    private const int MaxFiles = 3;
    private const long MaxFileBytes = 5 * 1024 * 1024;
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

                PruneLogFiles(directory, MaxFiles - 1);
                _currentPath = CreateLogPath(directory);
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

                if (_currentPath is not null &&
                    new FileInfo(_currentPath).Length >= MaxFileBytes)
                {
                    Rotate();
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

    private static void Rotate()
    {
        try
        {
            string directory = Path.GetDirectoryName(_currentPath!)!;
            _currentPath = CreateLogPath(directory);
            File.WriteAllText(
                _currentPath,
                $"===== 云曦PC统计 日志轮转 {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====={Environment.NewLine}");
            PruneLogFiles(directory);
        }
        catch
        {
        }
    }

    private static string CreateLogPath(string directory)
    {
        string baseName = $"CloudXiPCStatistics_{DateTime.Now:yyyyMMdd_HHmmss}";
        string path = Path.Combine(directory, baseName + ".log");
        int suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName}_{suffix}.log");
            suffix++;
        }
        return path;
    }

    private static void PruneLogFiles(string directory, int maxFiles = MaxFiles)
    {
        string[] existing = Directory.GetFiles(directory, "CloudXiPCStatistics_*.log");
        Array.Sort(existing, (a, b) => File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b)));
        int removeCount = existing.Length - maxFiles;
        for (int i = 0; i < removeCount && i < existing.Length; i++)
        {
            File.Delete(existing[i]);
        }
    }
}
