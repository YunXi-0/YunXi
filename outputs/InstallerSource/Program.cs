using System.Windows.Forms;
using System.Text.Json;

namespace CloudXiPcMonitor.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (TryRunSilent(args))
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerMainForm());
    }

    private static bool TryRunSilent(string[] args)
    {
        if (!args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string installDirectory = GetArgument(args, "--dir")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "云曦PC统计");
        string resultFile = GetArgument(args, "--result")
            ?? Path.Combine(Path.GetTempPath(), "cloudxi-installer-result.txt");
        bool autoStart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        bool desktop = args.Contains("--desktop", StringComparer.OrdinalIgnoreCase);
        bool run = args.Contains("--run", StringComparer.OrdinalIgnoreCase);
        int? waitProcessId = GetIntArgument(args, "--wait-pid");

        try
        {
            bool installed = InstallerCore.Install(
                installDirectory,
                autoStart,
                desktop,
                run,
                progress: null,
                waitProcessId: waitProcessId);
            if (!installed)
            {
                throw new InvalidOperationException("安装未完成。");
            }
            WriteResult(resultFile, true, null);
        }
        catch (Exception ex)
        {
            WriteResult(resultFile, false, ex.ToString());
            ApplicationConfiguration.Initialize();
            MessageBox.Show(
                $"更新安装失败：\r\n\r\n{ex.Message}\r\n\r\n安装器已保留，可稍后重试。",
                "云曦PC统计 更新失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        return true;
    }

    private static void WriteResult(string path, bool success, string? error)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
                {
                    success,
                    error,
                    completed_at_utc = DateTimeOffset.UtcNow,
                }));
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch
        {
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int? GetIntArgument(string[] args, string name)
    {
        string? value = GetArgument(args, name);
        return int.TryParse(value, out int processId) && processId > 0 ? processId : null;
    }
}
