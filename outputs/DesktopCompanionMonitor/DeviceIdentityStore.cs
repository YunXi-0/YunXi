using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PcCompanionMonitor;

internal static class DeviceIdentityStore
{
    private static string FilePath => Path.Combine(new DailyDataStore().DataDirectory, "device.json");

    public static string LoadUuid()
    {
        try
        {
            if (AtomicFile.TryDeserialize(FilePath, out DeviceFile? data))
            {
                return data?.Uuid ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    public static void SaveUuid(string uuid)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(new DeviceFile { Uuid = uuid }));
        }
        catch
        {
        }
    }

    public static string GetMachineFingerprint()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            object? value = key?.GetValue("MachineGuid");
            if (value is string machineGuid && !string.IsNullOrWhiteSpace(machineGuid))
            {
                return HashFingerprint(machineGuid.Trim());
            }
        }
        catch
        {
        }

        return HashFingerprint(Environment.MachineName);
    }

    private static string HashFingerprint(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("CloudXiPcStatistician:v1:" + value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class DeviceFile
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";
    }
}
