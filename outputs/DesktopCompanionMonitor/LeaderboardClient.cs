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

    public async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            LeaderboardData data = await GetAsync();
            return string.IsNullOrEmpty(data.LatestVersion) ? null : data.LatestVersion;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Dictionary<string, IReadOnlyList<LeaderboardEntry>>> GetBoardsAsync(
        DateTime date,
        bool includeLuck = true,
        bool includeCollections = true)
    {
        string[] metrics = GetMetrics(includeLuck, includeCollections);
        try
        {
            LeaderboardData data = await GetAsync();
            _cache = data;
            return await BuildBoardsAsync(data, date, includeLuck, includeCollections);
        }
        catch
        {
            if (_cache is null)
            {
                return metrics.ToDictionary(metric => metric, _ => (IReadOnlyList<LeaderboardEntry>)[]);
            }
            return await BuildBoardsAsync(_cache, date, includeLuck, includeCollections);
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
        return DeserializeValue<LeaderboardData>(json) ?? new LeaderboardData();
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
        DateTime date,
        bool includeLuck,
        bool includeCollections)
    {
        string[] metrics = GetMetrics(includeLuck, includeCollections);
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

            if (userData is null)
            {
                continue;
            }

            RemoveUserEntries(boards, uuid);
            string name = ResolveUserName(userData, uuid);
            foreach (string metric in DailyMetrics)
            {
                if (TryGetDailyValue(userData, date, metric, out double dailyValue))
                {
                    Upsert(boards[metric], uuid, name, dailyValue);
                }

                if (TrySumDailyValues(userData, date, metric, 7, out double sevenDayValue))
                {
                    Upsert(boards[$"{metric}7"], uuid, name, sevenDayValue);
                }

                if (TrySumDailyValues(userData, date, metric, 30, out double thirtyDayValue))
                {
                    Upsert(boards[$"{metric}30"], uuid, name, thirtyDayValue);
                }

                string totalMetric = metric == "active" ? "active_total" : $"{metric}_total";
                if (TrySumAllDailyValues(userData, date, metric, out double totalValue))
                {
                    Upsert(boards[totalMetric], uuid, name, totalValue);
                }
            }

            if (includeLuck && TryGetDailyValue(userData, date, "luck", out double luck))
            {
                Upsert(boards["luck"], uuid, name, luck);
            }

            if (includeCollections && TryGetLatestValue(userData, date, "collections", out double collections))
            {
                Upsert(boards["collections"], uuid, name, collections);
            }
        }

        return boards.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<LeaderboardEntry>)pair.Value
                .GroupBy(entry => entry.Uuid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(entry => entry.Value).First())
                .OrderByDescending(entry => entry.Value)
                .ToList());
    }

    private static readonly string[] DailyMetrics =
    [
        "active",
        "mouse_total",
        "mouse_left",
        "mouse_right",
        "keyboard",
    ];

    private static string ResolveUserName(UserDataBlob userData, string uuid)
    {
        if (!string.IsNullOrWhiteSpace(userData.Name))
        {
            return userData.Name;
        }

        return userData.Entries.Values
            .SelectMany(day => day.Values)
            .SelectMany(entries => entries)
            .Select(entry => entry.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? uuid;
    }

    private static bool TryGetDailyValue(
        UserDataBlob userData,
        DateTime date,
        string metric,
        out double value)
    {
        string dayKey = date.ToString("yyyy-MM-dd");
        value = 0;
        return userData.Entries.TryGetValue(dayKey, out Dictionary<string, List<LeaderboardEntryDto>>? day) &&
            TryGetValue(day, metric, out value);
    }

    private static bool TrySumDailyValues(
        UserDataBlob userData,
        DateTime date,
        string metric,
        int days,
        out double total)
    {
        total = 0;
        bool found = false;
        for (int offset = 0; offset < days; offset++)
        {
            if (TryGetDailyValue(userData, date.AddDays(-offset), metric, out double value))
            {
                total += value;
                found = true;
            }
        }
        return found;
    }

    private static bool TrySumAllDailyValues(
        UserDataBlob userData,
        DateTime date,
        string metric,
        out double total)
    {
        string lastDayKey = date.ToString("yyyy-MM-dd");
        total = 0;
        bool found = false;
        foreach (KeyValuePair<string, Dictionary<string, List<LeaderboardEntryDto>>> pair in userData.Entries)
        {
            if (pair.Key.Length == 10 &&
                string.CompareOrdinal(pair.Key, lastDayKey) <= 0 &&
                TryGetValue(pair.Value, metric, out double value))
            {
                total += value;
                found = true;
            }
        }
        return found;
    }

    private static bool TryGetLatestValue(
        UserDataBlob userData,
        DateTime date,
        string metric,
        out double value)
    {
        string lastDayKey = date.ToString("yyyy-MM-dd");
        foreach (KeyValuePair<string, Dictionary<string, List<LeaderboardEntryDto>>> pair in userData.Entries
            .Where(pair => pair.Key.Length == 10 && string.CompareOrdinal(pair.Key, lastDayKey) <= 0)
            .OrderByDescending(pair => pair.Key, StringComparer.Ordinal))
        {
            if (TryGetValue(pair.Value, metric, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetValue(
        Dictionary<string, List<LeaderboardEntryDto>> day,
        string metric,
        out double value)
    {
        value = 0;
        if (!day.TryGetValue(metric, out List<LeaderboardEntryDto>? entries) || entries.Count == 0)
        {
            return false;
        }

        value = entries.Max(entry => entry.Value);
        return true;
    }

    private static void Upsert(
        List<LeaderboardEntry> board,
        string uuid,
        string name,
        double value)
    {
        board.RemoveAll(entry => string.Equals(entry.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        board.Add(new LeaderboardEntry(uuid, name, value));
    }

    private static void RemoveUserEntries(
        Dictionary<string, List<LeaderboardEntry>> boards,
        string uuid)
    {
        foreach (List<LeaderboardEntry> board in boards.Values)
        {
            board.RemoveAll(entry => string.Equals(entry.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string[] GetMetrics(bool includeLuck, bool includeCollections)
    {
        List<string> metrics =
        [
            "active",
            "active7",
            "mouse_total",
            "mouse_total7",
            "mouse_left",
            "mouse_left7",
            "mouse_right",
            "mouse_right7",
            "keyboard",
            "keyboard7",
            "active30",
            "mouse_total30",
            "mouse_left30",
            "mouse_right30",
            "keyboard30",
            "active_total",
            "mouse_total_total",
            "mouse_left_total",
            "mouse_right_total",
            "keyboard_total",
        ];
        if (includeLuck)
        {
            metrics.Add("luck");
        }
        if (includeCollections)
        {
            metrics.Add("collections");
        }
        return [.. metrics];
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
        return DeserializeValue<UserDataBlob>(json);
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
            .Select(e => new LeaderboardEntry(
                string.IsNullOrEmpty(e.Uuid) ? e.Id : e.Uuid,
                string.IsNullOrEmpty(e.Name) ? e.Id : e.Name,
                e.Value))
            .ToList();
    }

    private static T? DeserializeValue<T>(string json)
    {
        if (!string.IsNullOrWhiteSpace(json) && json[0] == '"')
        {
            string? inner = JsonSerializer.Deserialize<string>(json);
            if (!string.IsNullOrWhiteSpace(inner))
            {
                json = inner;
            }
        }

        return JsonSerializer.Deserialize<T>(json);
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
