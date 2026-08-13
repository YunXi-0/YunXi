using System.Text;
using System.Text.Json;

namespace PcCompanionMonitor;

internal static class AtomicFile
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(false);

    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定数据文件目录。");
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        string backupPath = fullPath + ".bak";

        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                using StreamWriter writer = new(
                    stream,
                    encoding ?? DefaultEncoding,
                    4096,
                    leaveOpen: true);
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                try
                {
                    ReplaceWithRetry(temporaryPath, fullPath, backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(fullPath, backupPath, overwrite: true);
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }

    public static bool TryDeserialize<T>(string path, out T? value)
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                T? parsed = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate));
                if (parsed is not null)
                {
                    value = parsed;
                    return true;
                }
            }
            catch
            {
            }
        }

        value = default;
        return false;
    }

    private static void ReplaceWithRetry(string sourcePath, string destinationPath, string backupPath)
    {
        const int attemptCount = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < attemptCount)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }
}
