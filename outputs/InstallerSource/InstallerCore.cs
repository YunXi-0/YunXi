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
            WaitForProcessExit(processId, exePath, TimeSpan.FromSeconds(15));
        }
        KillRunningInstances(exePath);

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

        progress?.Report("安装完成");
        return !runAfterInstall || TryStartApplication(exePath, installDirectory);
    }

    private static void WriteApplication(string exePath)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using Stream resource = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(AppResourceName)
                    ?? throw new InvalidOperationException("安装包内未找到云曦PC统计应用文件。");
                using FileStream output = new(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
                resource.CopyTo(output);
                output.Flush(true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static bool TryStartApplication(string exePath, string workingDirectory)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory,
                });
                return true;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(300);
            }
        }
        return false;
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

    private static void WaitForProcessExit(int processId, string targetPath, TimeSpan timeout)
    {
        if (processId == Environment.ProcessId)
        {
            return;
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
                return;
            }

            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
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
