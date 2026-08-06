namespace PcCompanionMonitor;

internal sealed class DeviceIdentityService
{
    private readonly LeaderboardClient _client;
    private readonly string _fingerprint;
    private string? _uuid;

    public DeviceIdentityService(LeaderboardClient client)
    {
        _client = client;
        _fingerprint = DeviceIdentityStore.GetMachineFingerprint();
    }

    public async Task<string> GetUuidAsync()
    {
        if (!string.IsNullOrEmpty(_uuid))
        {
            return _uuid;
        }

        string cached = DeviceIdentityStore.LoadUuid();
        if (!string.IsNullOrEmpty(cached))
        {
            _uuid = cached;
            return cached;
        }

        string assigned = await _client.GetOrCreateUuidAsync(_fingerprint);
        _uuid = assigned;
        DeviceIdentityStore.SaveUuid(assigned);
        return assigned;
    }
}
