using System.Drawing;
using System.Runtime.InteropServices;

namespace PcCompanionMonitor;

internal readonly record struct MouseDwellSnapshot(
    bool Edge,
    bool Corner,
    bool Center);

internal sealed class MouseDwellTracker
{
    private int _centerStreakSeconds;

    public MouseDwellSnapshot Sample()
    {
        if (!GetCursorPos(out NativePoint nativePoint))
        {
            return default;
        }

        System.Drawing.Point point = new(nativePoint.X, nativePoint.Y);

        Screen screen = Screen.FromPoint(point);
        Rectangle bounds = screen.Bounds;
        int right = bounds.Right - 1;
        int bottom = bounds.Bottom - 1;

        bool edge = point.X == bounds.Left ||
                    point.X == right ||
                    point.Y == bounds.Top ||
                    point.Y == bottom;
        bool corner = (point.X == bounds.Left || point.X == right) &&
                      (point.Y == bounds.Top || point.Y == bottom);

        int centerWidth = Math.Max(1, (int)(bounds.Width * 0.05));
        int centerHeight = Math.Max(1, (int)(bounds.Height * 0.05));
        Rectangle center = new(
            bounds.Left + (bounds.Width - centerWidth) / 2,
            bounds.Top + (bounds.Height - centerHeight) / 2,
            centerWidth,
            centerHeight);

        bool inCenter = center.Contains(point);
        _centerStreakSeconds = inCenter ? _centerStreakSeconds + 1 : 0;
        bool centerActive = _centerStreakSeconds > 3;

        return new MouseDwellSnapshot(edge, corner, centerActive);
    }

    public void Reset() => _centerStreakSeconds = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
