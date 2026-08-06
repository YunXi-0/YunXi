using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal enum UpdateCheckResult
{
    NoUpdate,
    UpdateStarted,
    Failed,
}

internal static class UpdateService
{
    private const string GitHubOwner = "YunXi-0";
    private const string GitHubRepo = "YunXi";
    private const string GitHubApiUrl =
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string InstallerAssetName = "CloudXiPCMonitor-Setup.exe";
    private const string ExpectedSignerThumbprint = "0D4DD4051471B73B664C3FDD1346657E179FF1B8";
    private static readonly string[] DownloadMirrors =
    [
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
    ];

    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 0);

    private static int _busy;

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CloudXiPcMonitor");
    }

    public static async Task<UpdateCheckResult> CheckAndUpdateAsync(
        Action<string>? progress = null,
        Action<int>? progressPercent = null)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return UpdateCheckResult.Failed;
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
                return UpdateCheckResult.Failed;
            }

            if (release is null ||
                !Version.TryParse(NormalizeTag(release.TagName), out Version? latestVersion) ||
                latestVersion <= CurrentVersion)
            {
                return UpdateCheckResult.NoUpdate;
            }

            GitHubAsset? installerAsset = release.Assets.FirstOrDefault(
                asset => string.Equals(
                    asset.Name,
                    InstallerAssetName,
                    StringComparison.OrdinalIgnoreCase));
            if (installerAsset is null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl))
            {
                return UpdateCheckResult.Failed;
            }

            string tempPath = Path.Combine(
                Path.GetTempPath(),
                $"云曦PC统计安装程序-{latestVersion}.exe");
            try
            {
                progress?.Invoke("发现新版本，正在下载更新...");
                bool downloaded = false;
                foreach (string mirror in DownloadMirrors)
                {
                    try
                    {
                        await DownloadInstallerAsync(
                            mirror + installerAsset.BrowserDownloadUrl,
                            tempPath,
                            progressPercent);
                        if (!Authenticode.HasExpectedSigner(tempPath, ExpectedSignerThumbprint))
                        {
                            throw new InvalidOperationException("Signature mismatch");
                        }

                        downloaded = true;
                        break;
                    }
                    catch
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                }

                if (!downloaded)
                {
                    return UpdateCheckResult.Failed;
                }
            }
            catch
            {
                return UpdateCheckResult.Failed;
            }

            string resultPath = Path.Combine(Path.GetTempPath(), "cloudxi-update-result.txt");
            string installDirectory = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string arguments =
                $"--silent --dir \"{installDirectory}\" --result \"{resultPath}\" --run";
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = arguments,
                UseShellExecute = true,
            });
            return UpdateCheckResult.UpdateStarted;
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
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using CancellationTokenSource cts = new(TimeSpan.FromMinutes(20));
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        using FileStream output = File.Create(destination);
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

    private static string NormalizeTag(string tag)
    {
        string value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            return value[1..];
        }
        return value;
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
                    "if ($null -eq $sig.SignerCertificate) { exit 1 }; " +
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
