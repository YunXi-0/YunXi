using System.Drawing;
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

    public int X => _data.X;
    public int Y => _data.Y;
    public float Scale => _data.Scale;
    public bool SnapToEdge
    {
        get => _data.SnapToEdge;
        set
        {
            if (_data.SnapToEdge == value) return;
            _data.SnapToEdge = value;
            Save();
        }
    }
    public bool TopMost
    {
        get => _data.TopMost;
        set
        {
            if (_data.TopMost == value) return;
            _data.TopMost = value;
            Save();
        }
    }
    public bool DarkMode
    {
        get => _data.DarkMode;
        set
        {
            if (_data.DarkMode == value) return;
            _data.DarkMode = value;
            Save();
        }
    }
    public string LastVersion { get => _data.LastVersion; set { _data.LastVersion = value; Save(); } }
    public string LastNotifiedVersion { get => _data.LastNotifiedVersion; set { _data.LastNotifiedVersion = value; Save(); } }

    public bool HasSavedScale => _data.Scale is >= 0.5f and <= 2f;
    public bool HasSavedPosition => _data.HasPosition || _data.X != 0 || _data.Y != 0;

    public void SavePlacement(Point location, float scale)
    {
        _data.X = location.X;
        _data.Y = location.Y;
        _data.Scale = Math.Clamp(scale, 0.5f, 2f);
        _data.HasPosition = true;
        Save();
    }

    public void ResetScale()
    {
        _data.Scale = 1f;
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                _data = JsonSerializer.Deserialize<PositionData>(File.ReadAllText(_filePath)) ?? new();
                if (_data.Scale <= 0 && _data.Width > 0 && _data.Height > 0)
                {
                    float ratio = _data.Width / (float)_data.Height;
                    Size baseSize = Math.Abs(ratio - 1f) <= Math.Abs(ratio - 400f / 360f)
                        ? new Size(200, 200)
                        : new Size(400, 360);
                    _data.Scale = Math.Clamp(Math.Min(
                        _data.Width / (float)baseSize.Width,
                        _data.Height / (float)baseSize.Height), 0.5f, 2f);
                }
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            string tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data));
            File.Move(tmp, _filePath, true);
        }
        catch { }
    }

    private sealed class PositionData
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int Width { get; set; }
        [JsonPropertyName("h")] public int Height { get; set; }
        [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
        [JsonPropertyName("snap")] public bool SnapToEdge { get; set; }
        [JsonPropertyName("topmost")] public bool TopMost { get; set; }
        [JsonPropertyName("dark")] public bool DarkMode { get; set; }
        [JsonPropertyName("last_ver")] public string LastVersion { get; set; } = "";
        [JsonPropertyName("last_notified_ver")] public string LastNotifiedVersion { get; set; } = "";
        [JsonPropertyName("has_pos")] public bool HasPosition { get; set; }
    }
}
