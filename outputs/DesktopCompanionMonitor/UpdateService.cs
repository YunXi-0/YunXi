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
    private const string InstallerAssetName = "YunXiStatistician.exe";
    private const string ChecksumAssetName = InstallerAssetName + ".sha256";
    private const string ExpectedSignerThumbprint = "0D4DD4051471B73B664C3FDD1346657E179FF1B8";
    private static readonly string[] DownloadMirrors =
    [
        "https://gh-proxy.com/",
        "https://gh.xrgzs.top/",
        "https://ghfast.top/",
        "https://gh.ddlc.top/",
        "https://ghproxy.net/",
    ];

    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 2, 9);

    private static int _busy;
    private static bool _installing;

    public static bool IsInstalling => _installing;

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
                AppLog.Info($"获取最新版本成功：{(release is null ? "null" : release.TagName)}");
            }
            catch
            {
                AppLog.Info("获取最新版本失败");
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "无法连接更新服务器");
            }

            if (release is null ||
                !Version.TryParse(NormalizeTag(release.TagName), out Version? latestVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "更新版本信息无效");
            }

            if (latestVersion <= CurrentVersion)
            {
                AppLog.Info($"无需更新：最新={release.TagName}，当前={CurrentVersion}");
                return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, null, "当前已是最新版本");
            }

            GitHubAsset? installerAsset = release.Assets.FirstOrDefault(
                asset => string.Equals(
                    asset.Name,
                    InstallerAssetName,
                    StringComparison.OrdinalIgnoreCase));
            GitHubAsset? checksumAsset = release.Assets.FirstOrDefault(
                asset => string.Equals(
                    asset.Name,
                    ChecksumAssetName,
                    StringComparison.OrdinalIgnoreCase));
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
        catch
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "检测更新失败");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public static async Task<UpdateInstallResult> InstallUpdateAsync(
        UpdateInfo update,
        int waitProcessId,
        Action<int>? progressPercent = null)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return new UpdateInstallResult(false, "更新操作正在进行");
        }

        _installing = true;
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
                AppLog.Info($"开始下载更新：{update.InstallerUrl}");
                await DownloadInstallerAsync(update.InstallerUrl, installerPath, progressPercent);
                AppLog.Info("安装包下载完成");
            }
            else
            {
                AppLog.Info("使用已缓存的安装包");
            }

            string checksumText;
            try
            {
                checksumText = await DownloadTextAsync(update.ChecksumUrl);
            }
            catch
            {
                AppLog.Info("无法获取安装包校验文件");
                return new UpdateInstallResult(false, "无法获取校验文件，安装包已保留，请稍后重试");
            }

            if (!TryGetExpectedHash(checksumText, out string expectedHash))
            {
                return new UpdateInstallResult(false, "校验文件格式无效，安装包未运行");
            }

            if (!await VerifySha256Async(installerPath, expectedHash))
            {
                MoveInvalidInstaller(installerPath);
                AppLog.Info("安装包哈希校验失败，已隔离");
                return new UpdateInstallResult(false, "安装包校验失败，请重新下载");
            }

            if (!Authenticode.HasExpectedSigner(installerPath, ExpectedSignerThumbprint))
            {
                MoveInvalidInstaller(installerPath);
                AppLog.Info("安装包签名校验失败，已隔离");
                return new UpdateInstallResult(false, "安装包签名校验失败，请重新下载");
            }

            AppLog.Info("安装包签名校验通过");
            string resultPath = Path.Combine(cacheDirectory, "install-result.txt");
            string installDirectory = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string arguments =
                $"--silent --dir \"{installDirectory}\" --result \"{resultPath}\" --run --wait-pid {waitProcessId}";
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments,
                UseShellExecute = true,
            });
            AppLog.Info("已启动静默安装进程");
            return new UpdateInstallResult(true, "更新程序已启动");
        }
        catch
        {
            AppLog.Info("更新安装启动失败");
            return new UpdateInstallResult(
                false,
                installerPath is not null && File.Exists(installerPath)
                    ? "更新下载或启动失败，安装包已保留"
                    : "更新下载失败，请稍后重试");
        }
        finally
        {
            _installing = false;
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, GitHubApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cts.Token);
        return JsonSerializer.Deserialize<GitHubRelease>(json);
    }

    private static async Task DownloadInstallerAsync(
        string url,
        string destination,
        Action<int>? progressPercent)
    {
        string partial = destination + ".download";
        try
        {
            Exception? lastError = null;
            string[] sources = [url, .. DownloadMirrors.Select(mirror => mirror + url)];
            foreach (string source in sources)
            {
                AppLog.Info($"尝试下载地址：{source}");
                try
                {
                    await DownloadFromAsync(source, partial, progressPercent);
                    File.Move(partial, destination, true);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    AppLog.Info($"下载失败：{source}");
                    if (File.Exists(partial))
                    {
                        File.Delete(partial);
                    }
                }
            }

            throw lastError ?? new InvalidOperationException("所有镜像下载失败");
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private static async Task DownloadFromAsync(
        string url,
        string partial,
        Action<int>? progressPercent)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(20));
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        await using FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None);
        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using Stream input = await response.Content.ReadAsStreamAsync(cts.Token);
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cts.Token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cts.Token);
            totalRead += read;
            if (totalBytes > 0)
            {
                int percent = (int)Math.Min(100, totalRead * 100L / totalBytes);
                progressPercent?.Invoke(percent);
            }
        }
        await output.FlushAsync(cts.Token);
    }

    private static async Task<string> DownloadTextAsync(string url)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));
        using HttpResponseMessage response = await Http.GetAsync(url, cts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cts.Token);
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

    internal static class Authenticode
    {
        public static bool HasExpectedSigner(string filePath, string expectedThumbprint)
        {
            try
            {
                string escapedPath = filePath.Replace("'", "''");
                string command =
                    "$sig = Get-AuthenticodeSignature -LiteralPath '" + escapedPath + "'; " +
                    "if ($null -eq $sig -or $sig.Status -ne 'Valid' -or $null -eq $sig.SignerCertificate) { exit 1 }; " +
                    "if ($sig.SignerCertificate.Thumbprint -eq '" + expectedThumbprint + "') " +
                    "{ exit 0 } else { exit 2 }";

                ProcessStartInfo psi = new("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(command);

                using Process process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Unable to start powershell.exe");
                if (!process.WaitForExit(30_000))
                {
                    process.Kill();
                    return false;
                }
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
