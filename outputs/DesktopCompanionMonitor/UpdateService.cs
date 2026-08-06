using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
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

    public static async Task<UpdateCheckResult> CheckAndUpdateAsync()
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
                $"云曦PC监测安装程序-{latestVersion}.exe");
            try
            {
                await DownloadInstallerAsync(installerAsset.BrowserDownloadUrl, tempPath);
                GitHubAsset? checksumAsset = release.Assets.FirstOrDefault(
                    asset => string.Equals(
                        asset.Name,
                        ChecksumAssetName,
                        StringComparison.OrdinalIgnoreCase));
                if (checksumAsset is not null)
                {
                    string checksumText = await DownloadTextAsync(checksumAsset.BrowserDownloadUrl);
                    if (!await VerifySha256Async(tempPath, checksumText))
                    {
                        return UpdateCheckResult.Failed;
                    }
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
        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubRelease>(json);
    }

    private static async Task DownloadInstallerAsync(string url, string destination)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using FileStream output = File.Create(destination);
        await response.Content.CopyToAsync(output);
        await output.FlushAsync();
    }

    private static async Task<bool> VerifySha256Async(string path, string expected)
    {
        try
        {
            string expectedHash = expected
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            await using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream);
            string actual = Convert.ToHexString(hash).ToLowerInvariant();
            return string.Equals(
                actual,
                expectedHash.Trim().ToLowerInvariant(),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> DownloadTextAsync(string url)
    {
        using HttpResponseMessage response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
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
