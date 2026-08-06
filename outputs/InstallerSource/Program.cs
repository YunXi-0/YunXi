using System.Windows.Forms;

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
            InstallerCore.Install(
                installDirectory,
                autoStart,
                desktop,
                run,
                progress: null,
                waitProcessId: waitProcessId);
            File.WriteAllText(resultFile, "OK");
        }
        catch (Exception ex)
        {
            File.WriteAllText(resultFile, ex.ToString());
        }

        return true;
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
