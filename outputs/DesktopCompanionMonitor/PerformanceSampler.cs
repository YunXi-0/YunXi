using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PcCompanionMonitor;

internal sealed record PerformanceSnapshot(
    double CpuPercent,
    double CpuHz,
    int ThreadCount,
    int HandleCount,
    double MemoryMb,
    double MemoryPercent);

internal sealed class PerformanceSampler : IDisposable
{
    private readonly Process _process;
    private readonly double _baseHz;
    private DateTime _lastSample;
    private TimeSpan _lastCpu;

    public PerformanceSampler(Process process)
    {
        _process = process;
        _baseHz = ReadBaseHz();
    }

    public PerformanceSnapshot Sample()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan cpu = _process.TotalProcessorTime;
        double percent = 0;
        if (_lastSample != default)
        {
            double wall = (now - _lastSample).TotalSeconds;
            double used = (cpu - _lastCpu).TotalSeconds;
            if (wall > 0) percent = Math.Clamp(used / (wall * Environment.ProcessorCount) * 100, 0, 100);
        }
        _lastSample = now;
        _lastCpu = cpu;

        double totalMb = 0;
        MemoryStatusEx status = new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            totalMb = status.TotalPhys / 1048576.0;
        }
        _process.Refresh();
        double memMb = _process.PrivateMemorySize64 / 1048576.0;

        return new PerformanceSnapshot(
            percent,
            percent / 100.0 * _baseHz * Environment.ProcessorCount,
            _process.Threads.Count,
            _process.HandleCount,
            memMb,
            totalMb > 0 ? memMb / totalMb * 100 : 0);
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    private static double ReadBaseHz()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            object? value = key?.GetValue("~MHz");
            if (value is not null) return Convert.ToInt32(value) * 1_000_000.0;
        }
        catch
        {
        }
        return 3_000_000_000.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
