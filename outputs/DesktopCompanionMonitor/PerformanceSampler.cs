using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PcCompanionMonitor;

internal sealed record PerformanceSnapshot(
    double CpuPercent,
    double CpuHz,
    double GpuPercent,
    double GpuMemoryMb,
    double GpuMemoryPercent,
    double MemoryMb,
    double MemoryPercent,
    bool GpuAvailable);

internal sealed class PerformanceSampler : IDisposable
{
    private readonly Process _process;
    private readonly double _baseHz;
    private readonly List<PerformanceCounter> _gpuUtilizationCounters = [];
    private readonly List<PerformanceCounter> _gpuDedicatedCounters = [];
    private readonly List<PerformanceCounter> _gpuSharedCounters = [];
    private PerformanceCounter? _privateWorkingSetCounter;
    private bool _gpuInitialized;
    private bool _gpuAvailable;
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
        EnsureMemoryCounter();
        double memMb = _process.WorkingSet64 / 1048576.0;
        if (_privateWorkingSetCounter is not null)
        {
            try
            {
                double privateBytes = _privateWorkingSetCounter.NextValue();
                if (privateBytes > 0)
                {
                    memMb = privateBytes / 1048576.0;
                }
            }
            catch
            {
            }
        }

        InitializeGpuCounters();
        double gpuPercent = 0;
        double gpuDedicatedMb = 0;
        double gpuSharedMb = 0;
        foreach (PerformanceCounter counter in _gpuUtilizationCounters)
        {
            try { gpuPercent += counter.NextValue(); } catch { }
        }
        foreach (PerformanceCounter counter in _gpuDedicatedCounters)
        {
            try { gpuDedicatedMb += counter.NextValue(); } catch { }
        }
        foreach (PerformanceCounter counter in _gpuSharedCounters)
        {
            try { gpuSharedMb += counter.NextValue(); } catch { }
        }

        return new PerformanceSnapshot(
            percent,
            percent / 100.0 * _baseHz * Environment.ProcessorCount,
            Math.Clamp(gpuPercent, 0, 100),
            (gpuDedicatedMb + gpuSharedMb) / 1048576.0,
            0,
            memMb,
            totalMb > 0 ? memMb / totalMb * 100 : 0,
            _gpuAvailable);
    }

    public void Dispose()
    {
        foreach (PerformanceCounter counter in _gpuUtilizationCounters) counter.Dispose();
        foreach (PerformanceCounter counter in _gpuDedicatedCounters) counter.Dispose();
        foreach (PerformanceCounter counter in _gpuSharedCounters) counter.Dispose();
        _privateWorkingSetCounter?.Dispose();
        _process.Dispose();
    }

    private void EnsureMemoryCounter()
    {
        if (_privateWorkingSetCounter is not null)
        {
            return;
        }

        try
        {
            PerformanceCounterCategory category = new("Process");
            string[] instances = category.GetInstanceNames();
            string processName = _process.ProcessName;
            string? instance = instances.FirstOrDefault(
                name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
            instance ??= instances.FirstOrDefault(
                name => string.Equals(
                    name,
                    $"{processName}#{_process.Id}",
                    StringComparison.OrdinalIgnoreCase));
            if (instance is not null)
            {
                _privateWorkingSetCounter = new PerformanceCounter(
                    "Process",
                    "Working Set - Private",
                    instance,
                    readOnly: true);
            }
        }
        catch
        {
        }
    }

    private void InitializeGpuCounters()
    {
        if (_gpuInitialized)
        {
            return;
        }

        _gpuInitialized = true;
        try
        {
            string pidPrefix = $"pid_{_process.Id}_";
            PerformanceCounterCategory engineCategory = new("GPU Engine");
            if (engineCategory.CounterExists("Utilization Percentage"))
            {
                string[] instances = engineCategory.GetInstanceNames();
                _gpuAvailable = instances.Any(instance => instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase));
                foreach (string instance in instances)
                {
                    if (instance.StartsWith(pidPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _gpuUtilizationCounters.Add(new PerformanceCounter(
                            "GPU Engine",
                            "Utilization Percentage",
                            instance,
                            readOnly: true));
                    }
                }
            }

            PerformanceCounterCategory memoryCategory = new("GPU Process Memory");
            if (memoryCategory.CounterExists("Dedicated Usage") &&
                memoryCategory.CounterExists("Shared Usage"))
            {
                foreach (string instance in memoryCategory.GetInstanceNames())
                {
                    if (instance.StartsWith(pidPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _gpuDedicatedCounters.Add(new PerformanceCounter(
                            "GPU Process Memory",
                            "Dedicated Usage",
                            instance,
                            readOnly: true));
                        _gpuSharedCounters.Add(new PerformanceCounter(
                            "GPU Process Memory",
                            "Shared Usage",
                            instance,
                            readOnly: true));
                    }
                }
            }
        }
        catch
        {
        }
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
