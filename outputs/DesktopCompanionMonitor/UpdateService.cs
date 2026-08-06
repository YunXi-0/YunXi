using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal enum UpdateCheckStatus
{
    NoUpdate,
    Available,
    Failed,
}

internal sealed record UpdateInfo(Version Version, string InstallerUrl, string ChecksumUrl);

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info, string Message);

internal sealed record UpdateInstallResult(bool Started, string Message);

internal static class UpdateService
{
    private const string GitHubOwner = "YunXi-0";
    private const string GitHubRepo = "YunXi";
    private const string GitHubApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string InstallerAssetName = "CloudXiPCMonitor-Setup.exe";
    private const string ChecksumAssetName = InstallerAssetName + ".sha256";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 0);

    private static int _busy;

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CloudXiPcMonitor");
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "更新检查正在进行");
        }

        try
        {
            GitHubRelease? release;
            try
            {
                release = await FetchLatestReleaseAsync();
            }
            catch
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "无法连接更新服务器");
            }

            if (release is null ||
                !Version.TryParse(NormalizeTag(release.TagName), out Version? latestVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "更新版本信息无效");
            }

            if (latestVersion <= CurrentVersion)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, null, "当前已是最新版本");
            }

            GitHubAsset? installerAsset = release.Assets.FirstOrDefault(
                asset => string.Equals(asset.Name, InstallerAssetName, StringComparison.OrdinalIgnoreCase));
            GitHubAsset? checksumAsset = release.Assets.FirstOrDefault(
                asset => string.Equals(asset.Name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
            if (installerAsset is null || checksumAsset is null ||
                !IsHttpsUrl(installerAsset.BrowserDownloadUrl) ||
                !IsHttpsUrl(checksumAsset.BrowserDownloadUrl))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "更新包缺少有效校验文件");
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.Available,
                new UpdateInfo(latestVersion, installerAsset.BrowserDownloadUrl, checksumAsset.BrowserDownloadUrl),
                $"发现版本 {latestVersion}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public static async Task<UpdateInstallResult> InstallUpdateAsync(UpdateInfo update, int waitProcessId)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return new UpdateInstallResult(false, "更新操作正在进行");
        }

        string? installerPath = null;
        try
        {
            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CloudXiPcMonitor",
                "updates",
                update.Version.ToString());
            Directory.CreateDirectory(cacheDirectory);
            installerPath = Path.Combine(cacheDirectory, InstallerAssetName);

            if (!File.Exists(installerPath))
            {
                await DownloadInstallerAsync(update.InstallerUrl, installerPath);
            }

            string checksumText;
            try
            {
                checksumText = await DownloadTextAsync(update.ChecksumUrl);
            }
            catch
            {
                return new UpdateInstallResult(false, "无法获取校验文件，安装包已保留，请稍后重试");
            }

            if (!TryGetExpectedHash(checksumText, out string expectedHash))
            {
                return new UpdateInstallResult(false, "校验文件格式无效，安装包未运行");
            }

            if (!await VerifySha256Async(installerPath, expectedHash))
            {
                MoveInvalidInstaller(installerPath);
                return new UpdateInstallResult(false, "安装包校验失败，请重新下载");
            }

            string resultPath = Path.Combine(cacheDirectory, "install-result.txt");
            string arguments =
                $"--silent --dir \"{AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}\" " +
                $"--result \"{resultPath}\" --run --wait-pid {waitProcessId}";
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments,
                UseShellExecute = true,
            });
            return new UpdateInstallResult(true, "更新程序已启动");
        }
        catch
        {
            return new UpdateInstallResult(
                false,
                installerPath is not null && File.Exists(installerPath)
                    ? "更新下载或启动失败，安装包已保留"
                    : "更新下载失败，请稍后重试");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, GitHubApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubRelease>(json);
    }

    private static async Task DownloadInstallerAsync(string url, string destination)
    {
        string partial = destination + ".download";
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(output);
            await output.FlushAsync();
            File.Move(partial, destination, true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHash)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        string actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedHash, StringComparison.Ordinal);
    }

    private static bool TryGetExpectedHash(string text, out string hash)
    {
        string token = text
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
        {
            hash = "";
            return false;
        }

        hash = token.ToLowerInvariant();
        return true;
    }

    private static async Task<string> DownloadTextAsync(string url)
    {
        using HttpResponseMessage response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static void MoveInvalidInstaller(string path)
    {
        try
        {
            string invalidPath = path + ".invalid-" + Guid.NewGuid().ToString("N");
            File.Move(path, invalidPath, true);
        }
        catch
        {
        }
    }

    private static bool IsHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTag(string tag)
    {
        string value = tag.Trim();
        return value.StartsWith('v') || value.StartsWith('V') ? value[1..] : value;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    }
}
