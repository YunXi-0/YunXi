using System.Runtime.InteropServices;

namespace PcCompanionMonitor;

internal static class InputDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static bool HasRecentInput(TimeSpan within)
    {
        LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return false;
        uint now = unchecked((uint)Environment.TickCount);
        return unchecked(now - info.Time) <= (uint)within.TotalMilliseconds;
    }
}
