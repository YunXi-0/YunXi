using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed record LeaderboardEntry(string Uuid, string Name, double Value);

internal sealed class LeaderboardClient
{
    private const string KvdbBaseUrl = "https://kvdb.io/A2vqsiB5juK3mX6H9urPed";
    private const string RegistryKey = "registry";
    private const string UserKeyPrefix = "user_";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private LeaderboardData? _cache;

    public async Task<string> GetOrCreateUuidAsync(string fingerprint)
    {
        await _lock.WaitAsync();
        try
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                LeaderboardData data;
                try
                {
                    data = await GetAsync();
                }
                catch
                {
                    await Task.Delay(200 * (attempt + 1));
                    continue;
                }

                if (data.UuidMap.TryGetValue(fingerprint, out string? existing) &&
                    !string.IsNullOrEmpty(existing))
                {
                    _cache = data;
                    return existing;
                }

                string uuid = data.UuidCounter.ToString("D3");
                data.UuidMap[fingerprint] = uuid;
                data.UuidCounter++;
                if (await PutAsync(data))
                {
                    _cache = data;
                    return uuid;
                }

                await Task.Delay(200 * (attempt + 1));
            }

            throw new InvalidOperationException("无法分配UUid");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, IReadOnlyList<LeaderboardEntry>>> GetBoardsAsync(DateTime date)
    {
        string[] metrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard"];
        try
        {
            LeaderboardData data = await GetAsync();
            _cache = data;
            return await BuildBoardsAsync(data, date);
        }
        catch
        {
            if (_cache is null)
            {
                return metrics.ToDictionary(metric => metric, _ => (IReadOnlyList<LeaderboardEntry>)[]);
            }
            return await BuildBoardsAsync(_cache, date);
        }
    }

    public async Task<bool> SubmitAllAsync(
        string uuid,
        string displayName,
        DateTime date,
        IReadOnlyDictionary<string, double> values)
    {
        await _lock.WaitAsync();
        try
        {
            string dayKey = date.ToString("yyyy-MM-dd");
            string userKey = $"{UserKeyPrefix}{uuid}";
            UserDataBlob? userData;
            try
            {
                userData = await GetUserDataAsync(userKey);
            }
            catch
            {
                return false;
            }

            userData ??= new UserDataBlob { Uuid = uuid, Name = displayName };
            userData.Uuid = uuid;
            userData.Name = displayName;
            if (!userData.Entries.TryGetValue(dayKey, out Dictionary<string, List<LeaderboardEntryDto>>? days))
            {
                days = [];
                userData.Entries[dayKey] = days;
            }

            foreach (KeyValuePair<string, double> pair in values)
            {
                string metricKey = pair.Key.ToLowerInvariant();
                days[metricKey] = [new LeaderboardEntryDto
                {
                    Uuid = uuid,
                    Name = displayName,
                    Value = pair.Value,
                }];
            }

            return await PutUserDataAsync(userKey, userData);
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<LeaderboardData> GetAsync()
    {
        using HttpResponseMessage response = await Http.GetAsync($"{KvdbBaseUrl}/{RegistryKey}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new LeaderboardData();
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LeaderboardData>(json) ?? new LeaderboardData();
    }

    private async Task<bool> PutAsync(LeaderboardData data)
    {
        string inner = JsonSerializer.Serialize(data);
        string json = JsonSerializer.Serialize(inner);
        using HttpRequestMessage request = new(HttpMethod.Put, $"{KvdbBaseUrl}/{RegistryKey}")
        {
            Content = new StringContent(json, Encoding.UTF8, "text/plain"),
        };
        using HttpResponseMessage response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        _cache = data;
        return true;
    }

    private static async Task<Dictionary<string, IReadOnlyList<LeaderboardEntry>>> BuildBoardsAsync(
        LeaderboardData data,
        DateTime date)
    {
        string[] metrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard"];
        string dayKey = date.ToString("yyyy-MM-dd");
        var boards = metrics.ToDictionary(
            metric => metric,
            metric => Extract(data, metric, date).ToList());

        foreach (string uuid in data.UuidMap.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            UserDataBlob? userData;
            try
            {
                userData = await GetUserDataAsync($"{UserKeyPrefix}{uuid}");
            }
            catch
            {
                continue;
            }

            if (userData is null ||
                !userData.Entries.TryGetValue(dayKey, out Dictionary<string, List<LeaderboardEntryDto>>? days))
            {
                continue;
            }

            foreach (string metric in metrics)
            {
                if (!days.TryGetValue(metric, out List<LeaderboardEntryDto>? entries))
                {
                    continue;
                }

                foreach (LeaderboardEntryDto entry in entries)
                {
                    boards[metric].Add(new LeaderboardEntry(
                        string.IsNullOrEmpty(entry.Uuid) ? entry.Id : entry.Uuid,
                        string.IsNullOrEmpty(entry.Name) ? entry.Id : entry.Name,
                        entry.Value));
                }

                boards[metric] = boards[metric]
                    .GroupBy(entry => entry.Uuid, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(entry => entry.Value).First())
                    .OrderByDescending(entry => entry.Value)
                    .Take(5)
                    .ToList();
            }
        }

        return boards.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<LeaderboardEntry>)pair.Value);
    }

    private static async Task<UserDataBlob?> GetUserDataAsync(string key)
    {
        using HttpResponseMessage response = await Http.GetAsync($"{KvdbBaseUrl}/{key}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<UserDataBlob>(json);
    }

    private static async Task<bool> PutUserDataAsync(string key, UserDataBlob data)
    {
        string inner = JsonSerializer.Serialize(data);
        string json = JsonSerializer.Serialize(inner);
        using HttpRequestMessage request = new(HttpMethod.Put, $"{KvdbBaseUrl}/{key}")
        {
            Content = new StringContent(json, Encoding.UTF8, "text/plain"),
        };
        using HttpResponseMessage response = await Http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static IReadOnlyList<LeaderboardEntry> Extract(
        LeaderboardData data,
        string metric,
        DateTime date)
    {
        string dayKey = date.ToString("yyyy-MM-dd");
        if (!data.Entries.TryGetValue(dayKey, out Dictionary<string, List<LeaderboardEntryDto>>? days))
        {
            return [];
        }

        if (!days.TryGetValue(metric.ToLowerInvariant(), out List<LeaderboardEntryDto>? list))
        {
            return [];
        }

        return list
            .OrderByDescending(e => e.Value)
            .Take(5)
            .Select(e => new LeaderboardEntry(
                string.IsNullOrEmpty(e.Uuid) ? e.Id : e.Uuid,
                string.IsNullOrEmpty(e.Name) ? e.Id : e.Name,
                e.Value))
            .ToList();
    }

    private sealed class LeaderboardData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("uuid_counter")]
        public int UuidCounter { get; set; }

        [JsonPropertyName("uuid_map")]
        public Dictionary<string, string> UuidMap { get; set; } = [];

        [JsonPropertyName("entries")]
        public Dictionary<string, Dictionary<string, List<LeaderboardEntryDto>>> Entries { get; set; } = [];

        [JsonPropertyName("user_blobs")]
        public Dictionary<string, string> UserBlobs { get; set; } = [];

        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; } = "";

        [JsonPropertyName("installer_url")]
        public string InstallerUrl { get; set; } = "";

        [JsonPropertyName("installer_sha256")]
        public string InstallerSha256 { get; set; } = "";
    }

    private sealed class UserDataBlob
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("entries")]
        public Dictionary<string, Dictionary<string, List<LeaderboardEntryDto>>> Entries { get; set; } = [];
    }

    private sealed class LeaderboardEntryDto
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}
