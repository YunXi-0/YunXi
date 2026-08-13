using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcCompanionMonitor;

internal readonly record struct AppUsageSnapshot(bool QqActive, bool WeChatActive);

internal sealed class ForegroundAppUsageTracker
{
    private static readonly HashSet<string> QqNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "QQ.exe",
        "TIM.exe",
    };

    private static readonly HashSet<string> WeChatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WeChat.exe",
        "Weixin.exe",
    };

    public AppUsageSnapshot Sample()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return default;
        }

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return default;
        }

        string processName;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            return default;
        }

        string exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";

        if (!InputDetector.HasRecentInput(TimeSpan.FromSeconds(5)))
        {
            return default;
        }

        return new AppUsageSnapshot(
            QqNames.Contains(exeName),
            WeChatNames.Contains(exeName));
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
