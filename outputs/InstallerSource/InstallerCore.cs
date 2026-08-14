using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CloudXiPcMonitor.Installer;

internal static class InstallerCore
{
    private const string AppExeName = "云曦PC统计.exe";
    private const string AppResourceName = "CloudXiPcMonitor.App.exe";

    public static bool Install(
        string installDirectory,
        bool autoStart,
        bool createDesktopShortcut,
        bool runAfterInstall,
        IProgress<string>? progress,
        int? waitProcessId = null)
    {
        progress?.Report("准备安装目录...");
        Directory.CreateDirectory(installDirectory);

        string exePath = Path.Combine(installDirectory, AppExeName);
        if (waitProcessId is int processId)
        {
            if (!WaitForProcessExit(processId, exePath, TimeSpan.FromSeconds(15)))
            {
                throw new IOException("旧版本进程未能在规定时间内退出。");
            }
        }
        KillRunningInstances(exePath);
        WaitForTargetAvailable(exePath, TimeSpan.FromSeconds(15));

        string backupPath = exePath + ".previous";
        bool hadExistingApplication = File.Exists(exePath);
        if (hadExistingApplication)
        {
            File.Copy(exePath, backupPath, true);
        }

        try
        {
            progress?.Report("正在复制应用文件...");
            WriteApplication(exePath);
            Directory.CreateDirectory(Path.Combine(installDirectory, "data"));

            if (createDesktopShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(desktop, "云曦PC统计.lnk", exePath);
            }

            if (autoStart)
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                CreateShortcut(startup, "云曦PC统计.lnk", exePath);
            }

            if (runAfterInstall && !TryStartApplication(exePath, installDirectory, verifyStartup: true))
            {
                throw new InvalidOperationException("新版本启动失败，已恢复旧版本。");
            }

            TryDelete(backupPath);
            progress?.Report("安装完成");
            return true;
        }
        catch (Exception installError)
        {
            Exception? rollbackError = null;
            bool applicationRestored = false;
            try
            {
                RestoreApplication(exePath, backupPath, hadExistingApplication);
                applicationRestored = true;
            }
            catch (Exception ex)
            {
                rollbackError = ex;
            }
            if (runAfterInstall && hadExistingApplication && applicationRestored)
            {
                TryStartApplication(exePath, installDirectory, verifyStartup: false);
            }
            if (rollbackError is not null)
            {
                throw new IOException(
                    "安装失败，且自动回滚未全部完成。",
                    new AggregateException(installError, rollbackError));
            }
            throw;
        }
    }

    private static void WriteApplication(string exePath)
    {
        string temporaryPath = exePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using Stream resource = Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(AppResourceName)
                        ?? throw new InvalidOperationException("安装包内未找到云曦PC统计应用文件。");
                    using (FileStream output = new(
                        temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        resource.CopyTo(output);
                        output.Flush(true);
                    }

                    File.Move(temporaryPath, exePath, true);

                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    TryDelete(temporaryPath);
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    TryDelete(temporaryPath);
                    Thread.Sleep(500);
                }
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryStartApplication(
        string exePath,
        string workingDirectory,
        bool verifyStartup)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory,
                });
                if (process is null)
                {
                    continue;
                }
                return !verifyStartup || !process.WaitForExit(3000);
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
        }
        return false;
    }

    private static void RestoreApplication(
        string exePath,
        string backupPath,
        bool hadExistingApplication)
    {
        KillRunningInstances(exePath);
        WaitForTargetAvailable(exePath, TimeSpan.FromSeconds(15));
        if (!hadExistingApplication)
        {
            TryDelete(exePath);
            return;
        }

        if (!File.Exists(backupPath))
        {
            throw new IOException("更新失败，且旧版本备份不存在，无法自动恢复。");
        }

        string restorePath = exePath + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(backupPath, restorePath, true);
            File.Move(restorePath, exePath, true);
            TryDelete(backupPath);
        }
        finally
        {
            TryDelete(restorePath);
        }
    }

    private static void KillRunningInstances(string exePath)
    {
        foreach (Process process in Process.GetProcesses())
        {
            string? fileName = null;
            try
            {
                if (!process.HasExited)
                {
                    fileName = process.MainModule?.FileName;
                }
            }
            catch
            {
                fileName = null;
            }

            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                string fullName = Path.GetFullPath(fileName);
                string fullTarget = Path.GetFullPath(exePath);
                    if (fullName.Equals(fullTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            process.CloseMainWindow();
                        }
                        catch
                        {
                        }

                        if (!process.WaitForExit(5000) && !process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool WaitForProcessExit(int processId, string targetPath, TimeSpan timeout)
    {
        if (processId == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            string? fileName = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(fileName) ||
                !Path.GetFullPath(fileName).Equals(
                    Path.GetFullPath(targetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return process.WaitForExit((int)timeout.TotalMilliseconds) || process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void WaitForTargetAvailable(string exePath, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (File.Exists(exePath) && DateTime.UtcNow < deadline)
        {
            try
            {
                using FileStream stream = new(exePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        if (File.Exists(exePath))
        {
            throw new IOException("旧版本程序文件仍被其他进程占用。");
        }
    }

    private static void TryDelete(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
            catch
            {
                return;
            }
        }
    }

    private static void CreateShortcut(string folder, string name, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        string shortcutPath = Path.Combine(folder, name);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建快捷方式。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = "云曦PC统计";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

}
