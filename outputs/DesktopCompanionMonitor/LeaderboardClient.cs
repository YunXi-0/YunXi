using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed record LeaderboardEntry(string Uuid, string Name, double Value);

internal sealed record LeaderboardBoardsResult(
    Dictionary<string, IReadOnlyList<LeaderboardEntry>> Boards,
    bool FromCache,
    DateTimeOffset? CachedAtUtc);

internal sealed class LeaderboardClient
{
    private const string KvdbBaseUrl = "https://kvdb.io/A2vqsiB5juK3mX6H9urPed";
    private const string RegistryKey = "registry";
    private const string UserKeyPrefix = "user_";
    private const string CompatibilityTotalsKey = "_totals";
    private const string CompatibilityThroughMetric = "_through";
    private const int MaxConcurrentUserFetches = 4;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _boardsCachePath;
    private LeaderboardData? _cache;
    private BoardsCacheFile? _boardsCache;

    public LeaderboardClient(string dataDirectory)
    {
        _boardsCachePath = Path.Combine(dataDirectory, "leaderboard_cache.json");
        AtomicFile.TryDeserialize(_boardsCachePath, out _boardsCache);
    }

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

    public async Task<LeaderboardBoardsResult> GetBoardsAsync(
        DateTime date,
        bool includeLuck = true,
        bool includeCollections = true)
    {
        string[] metrics = GetMetrics(includeLuck, includeCollections);
        try
        {
            LeaderboardData data = await GetAsync();
            Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards =
                await BuildBoardsAsync(data, date, includeLuck, includeCollections);
            _cache = data;
            SaveBoardsCache(date, boards);
            return new LeaderboardBoardsResult(boards, false, _boardsCache?.CachedAtUtc);
        }
        catch
        {
            Dictionary<string, IReadOnlyList<LeaderboardEntry>> cachedBoards =
                GetCachedBoards(date, metrics, out DateTimeOffset? cachedAtUtc);
            return new LeaderboardBoardsResult(cachedBoards, true, cachedAtUtc);
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
            UpdateUserData(userData, uuid, displayName, date, values);

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

        string[] uuids = [.. data.UuidMap.Values.Distinct(StringComparer.OrdinalIgnoreCase)];
        using SemaphoreSlim fetchLimit = new(MaxConcurrentUserFetches, MaxConcurrentUserFetches);
        Task<UserFetchResult>[] fetchTasks =
        [
            .. uuids.Select(uuid => GetUserDataSafelyAsync(uuid, fetchLimit)),
        ];

        UserFetchResult[] results = await Task.WhenAll(fetchTasks);
        if (results.Any(result => !result.Success))
        {
            throw new HttpRequestException("排行榜用户数据未完整加载");
        }

        foreach (UserFetchResult result in results)
        {
            string uuid = result.Uuid;
            UserDataBlob? userData = result.Data;
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
                if (TryGetAllTimeValue(userData, date, metric, out double totalValue))
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

    private static async Task<UserFetchResult> GetUserDataSafelyAsync(
        string uuid,
        SemaphoreSlim fetchLimit)
    {
        await fetchLimit.WaitAsync();
        try
        {
            return new UserFetchResult(uuid, true, await GetUserDataAsync($"{UserKeyPrefix}{uuid}"));
        }
        catch (Exception ex)
        {
            AppLog.Info($"排行榜用户数据读取失败（UUID={uuid}）：{ex.Message}");
            return new UserFetchResult(uuid, false, null);
        }
        finally
        {
            fetchLimit.Release();
        }
    }

    private static readonly string[] DailyMetrics =
    [
        "active",
        "mouse_total",
        "mouse_left",
        "mouse_right",
        "keyboard",
    ];

    private static void UpdateUserData(
        UserDataBlob userData,
        string uuid,
        string displayName,
        DateTime date,
        IReadOnlyDictionary<string, double> values)
    {
        userData.Uuid = uuid;
        userData.Name = displayName;
        UpgradeUserData(userData, date);

        string dayKey = date.ToString("yyyy-MM-dd");
        if (!userData.Entries.TryGetValue(dayKey, out Dictionary<string, List<LeaderboardEntryDto>>? day))
        {
            day = [];
            userData.Entries[dayKey] = day;
        }

        foreach (string metric in DailyMetrics)
        {
            if (!values.TryGetValue(metric, out double newValue))
            {
                continue;
            }

            double oldValue = TryGetValue(day, metric, out double existingValue) ? existingValue : 0;
            userData.Totals[metric] = userData.Totals.GetValueOrDefault(metric) + newValue - oldValue;
            day[metric] = [new LeaderboardEntryDto
            {
                Uuid = uuid,
                Name = displayName,
                Value = newValue,
            }];
        }

        SaveOptionalDailyValue(day, values, "luck", uuid, displayName);
        SaveOptionalDailyValue(day, values, "collections", uuid, displayName);
        RemoveDerivedMetrics(userData);
        SaveCompatibilityTotals(userData, uuid, displayName, date);
        PruneUserEntries(userData, date);
    }

    private static void UpgradeUserData(UserDataBlob userData, DateTime date)
    {
        if (TryReadCompatibilityTotals(
                userData,
                out Dictionary<string, double>? compatibilityTotals,
                out DateTime? compatibilityThrough))
        {
            userData.Totals = compatibilityTotals;
            userData.SchemaVersion = 2;
            if (compatibilityThrough is { } through)
            {
                AddDailyValuesAfter(userData, through, date, userData.Totals);
            }
        }

        if (userData.SchemaVersion >= 2)
        {
            foreach (string metric in DailyMetrics)
            {
                userData.Totals.TryAdd(metric, 0);
            }
            return;
        }

        foreach (string metric in DailyMetrics)
        {
            double total = 0;
            foreach (KeyValuePair<string, Dictionary<string, List<LeaderboardEntryDto>>> entry in userData.Entries)
            {
                if (TryParseDayKey(entry.Key, out DateTime entryDate) &&
                    entryDate <= date.Date &&
                    TryGetValue(entry.Value, metric, out double value))
                {
                    total += value;
                }
            }
            userData.Totals[metric] = total;
        }
        userData.SchemaVersion = 2;
    }

    private static void SaveOptionalDailyValue(
        Dictionary<string, List<LeaderboardEntryDto>> day,
        IReadOnlyDictionary<string, double> values,
        string metric,
        string uuid,
        string name)
    {
        if (values.TryGetValue(metric, out double value))
        {
            day[metric] = [new LeaderboardEntryDto
            {
                Uuid = uuid,
                Name = name,
                Value = value,
            }];
        }
    }

    private static void RemoveDerivedMetrics(UserDataBlob userData)
    {
        HashSet<string> retainedMetrics = [.. DailyMetrics, "luck", "collections"];
        foreach (Dictionary<string, List<LeaderboardEntryDto>> day in userData.Entries.Values)
        {
            foreach (string metric in day.Keys.Where(metric => !retainedMetrics.Contains(metric)).ToList())
            {
                day.Remove(metric);
            }
        }
    }

    private static void PruneUserEntries(UserDataBlob userData, DateTime date)
    {
        DateTime cutoff = date.Date.AddDays(-29);
        foreach (string dayKey in userData.Entries.Keys.ToList())
        {
            if (dayKey == CompatibilityTotalsKey)
            {
                continue;
            }
            if (!TryParseDayKey(dayKey, out DateTime entryDate) ||
                entryDate < cutoff ||
                entryDate > date.Date)
            {
                userData.Entries.Remove(dayKey);
            }
        }
    }

    private static bool TryReadCompatibilityTotals(
        UserDataBlob userData,
        out Dictionary<string, double> totals,
        out DateTime? throughDate)
    {
        totals = [];
        throughDate = null;
        if (!userData.Entries.TryGetValue(
                CompatibilityTotalsKey,
                out Dictionary<string, List<LeaderboardEntryDto>>? compatibility))
        {
            return false;
        }

        foreach (string metric in DailyMetrics)
        {
            if (TryGetValue(compatibility, metric, out double value))
            {
                totals[metric] = value;
            }
        }
        if (TryGetValue(compatibility, CompatibilityThroughMetric, out double encodedDate) &&
            DateTime.TryParseExact(
                ((long)encodedDate).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime parsedDate))
        {
            throughDate = parsedDate;
        }
        return totals.Count > 0;
    }

    private static void AddDailyValuesAfter(
        UserDataBlob userData,
        DateTime throughDate,
        DateTime currentDate,
        Dictionary<string, double> totals)
    {
        foreach (KeyValuePair<string, Dictionary<string, List<LeaderboardEntryDto>>> entry in userData.Entries)
        {
            if (!TryParseDayKey(entry.Key, out DateTime entryDate) ||
                entryDate <= throughDate.Date ||
                entryDate > currentDate.Date)
            {
                continue;
            }

            foreach (string metric in DailyMetrics)
            {
                if (TryGetValue(entry.Value, metric, out double value))
                {
                    totals[metric] = totals.GetValueOrDefault(metric) + value;
                }
            }
        }
    }

    private static void SaveCompatibilityTotals(
        UserDataBlob userData,
        string uuid,
        string name,
        DateTime throughDate)
    {
        var compatibility = new Dictionary<string, List<LeaderboardEntryDto>>();
        foreach (string metric in DailyMetrics)
        {
            compatibility[metric] = [new LeaderboardEntryDto
            {
                Uuid = uuid,
                Name = name,
                Value = userData.Totals.GetValueOrDefault(metric),
            }];
        }
        compatibility[CompatibilityThroughMetric] = [new LeaderboardEntryDto
        {
            Uuid = uuid,
            Name = name,
            Value = int.Parse(
                throughDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture),
        }];
        userData.Entries[CompatibilityTotalsKey] = compatibility;
    }

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
            if (TryParseDayKey(pair.Key, out _) &&
                string.CompareOrdinal(pair.Key, lastDayKey) <= 0 &&
                TryGetValue(pair.Value, metric, out double value))
            {
                total += value;
                found = true;
            }
        }
        return found;
    }

    private static bool TryGetAllTimeValue(
        UserDataBlob userData,
        DateTime date,
        string metric,
        out double total)
    {
        if (userData.SchemaVersion >= 2 && userData.Totals.TryGetValue(metric, out total))
        {
            return true;
        }

        return TrySumAllDailyValues(userData, date, metric, out total);
    }
    private static bool TryGetLatestValue(
        UserDataBlob userData,
        DateTime date,
        string metric,
        out double value)
    {
        string lastDayKey = date.ToString("yyyy-MM-dd");
        foreach (KeyValuePair<string, Dictionary<string, List<LeaderboardEntryDto>>> pair in userData.Entries
            .Where(pair => TryParseDayKey(pair.Key, out _) && string.CompareOrdinal(pair.Key, lastDayKey) <= 0)
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

    private static bool TryParseDayKey(string value, out DateTime date)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);
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

    private void SaveBoardsCache(
        DateTime date,
        Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards)
    {
        var cache = new BoardsCacheFile
        {
            Date = date.ToString("yyyy-MM-dd"),
            CachedAtUtc = DateTimeOffset.UtcNow,
            Boards = boards.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(entry => new LeaderboardEntryDto
                {
                    Uuid = entry.Uuid,
                    Name = entry.Name,
                    Value = entry.Value,
                }).ToList()),
        };
        _boardsCache = cache;

        try
        {
            AtomicFile.WriteAllText(_boardsCachePath, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    private Dictionary<string, IReadOnlyList<LeaderboardEntry>> GetCachedBoards(
        DateTime date,
        IReadOnlyList<string> metrics,
        out DateTimeOffset? cachedAtUtc)
    {
        cachedAtUtc = null;
        if (_boardsCache is null ||
            !DateTime.TryParseExact(
                _boardsCache.Date,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime cachedDate) ||
            cachedDate.Date != date.Date)
        {
            return metrics.ToDictionary(metric => metric, _ => (IReadOnlyList<LeaderboardEntry>)[]);
        }

        cachedAtUtc = _boardsCache.CachedAtUtc;
        return metrics.ToDictionary(
            metric => metric,
            metric => (IReadOnlyList<LeaderboardEntry>)(_boardsCache.Boards.TryGetValue(
                metric,
                out List<LeaderboardEntryDto>? entries)
                    ? entries.Select(entry => new LeaderboardEntry(
                        string.IsNullOrEmpty(entry.Uuid) ? entry.Id : entry.Uuid,
                        string.IsNullOrEmpty(entry.Name) ? entry.Id : entry.Name,
                        entry.Value)).ToList()
                    : []));
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
        const int maxStringLayers = 4;
        for (int layer = 0; layer < maxStringLayers; layer++)
        {
            if (string.IsNullOrWhiteSpace(json) ||
                string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                return default;
            }

            string trimmed = json.TrimStart();
            if (trimmed[0] != '"')
            {
                break;
            }

            string? inner = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrWhiteSpace(inner))
            {
                return default;
            }
            json = inner;
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
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("totals")]
        public Dictionary<string, double> Totals { get; set; } = [];

        [JsonPropertyName("entries")]
        public Dictionary<string, Dictionary<string, List<LeaderboardEntryDto>>> Entries { get; set; } = [];
    }

    private sealed class BoardsCacheFile
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("cached_at_utc")]
        public DateTimeOffset CachedAtUtc { get; set; }

        [JsonPropertyName("boards")]
        public Dictionary<string, List<LeaderboardEntryDto>> Boards { get; set; } = [];
    }

    private sealed record UserFetchResult(string Uuid, bool Success, UserDataBlob? Data);

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
