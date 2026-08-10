using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed class AppPositionStore
{
    private readonly string _filePath;
    private PositionData _data = new();

    public AppPositionStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "app_position.json");
        Directory.CreateDirectory(dataDirectory);
        Load();
    }

    public int X { get => _data.X; set { _data.X = value; Save(); } }
    public int Y { get => _data.Y; set { _data.Y = value; Save(); } }
    public int Width { get => _data.Width; set { _data.Width = value; Save(); } }
    public int Height { get => _data.Height; set { _data.Height = value; Save(); } }
    public bool SnapToEdge { get => _data.SnapToEdge; set { _data.SnapToEdge = value; Save(); } }
    public string LastVersion { get => _data.LastVersion; set { _data.LastVersion = value; Save(); } }
    public string LastNotifiedVersion { get => _data.LastNotifiedVersion; set { _data.LastNotifiedVersion = value; Save(); } }

    public bool HasSavedSize => _data.Width > 0 && _data.Height > 0;
    public bool HasSavedPosition => _data.X != 0 || _data.Y != 0;

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                _data = JsonSerializer.Deserialize<PositionData>(File.ReadAllText(_filePath)) ?? new();
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data));
        }
        catch { }
    }

    private sealed class PositionData
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int Width { get; set; }
        [JsonPropertyName("h")] public int Height { get; set; }
        [JsonPropertyName("snap")] public bool SnapToEdge { get; set; }
        [JsonPropertyName("last_ver")] public string LastVersion { get; set; } = "";
        [JsonPropertyName("last_notified_ver")] public string LastNotifiedVersion { get; set; } = "";
    }
}
