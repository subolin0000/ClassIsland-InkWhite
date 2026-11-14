using System.Text.Json;
namespace AdaptiveText;
public sealed class Options
{
    public int FormatVersion { get; set; } = 1;
    public int ClassroomStableMs { get; set; } = 3000;
    public int ClassroomFadeMs { get; set; } = 2000;
    public bool Enabled { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;
    public bool Shadow { get; set; } = true;
    public string Preset { get; set; } = ColorPresets.Balanced;
}
public sealed class SettingsStore
{
    private readonly string _path;
    public Options Value { get; }
    public string? LoadError { get; }
    public event Action? Changed;
    public SettingsStore(string path)
    {
        _path = path;
        var hasExistingFile = File.Exists(path);
        try
        {
            Value = hasExistingFile ? JsonSerializer.Deserialize<Options>(File.ReadAllText(path)) ?? new() : new();
            // Earlier classroom versions used format 0, and very old realtime configs may omit the field.
            if (hasExistingFile && (Value.FormatVersion < 1 || Value.Preset == "Realtime")) { Value.Shadow = false; Value.FormatVersion = 1; }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { Value = new(); LoadError = e.Message; }
        Normalize();
    }
    private void Normalize()
    {
        Value.ClassroomStableMs = Math.Clamp(Value.ClassroomStableMs, 0, 30000);
        Value.ClassroomFadeMs = Math.Clamp(Value.ClassroomFadeMs, 500, 10000);
        Value.IntervalMs = Math.Clamp(Value.IntervalMs, 1000, 2000);
        Value.Preset = ColorPresets.Normalize(Value.Preset);
    }
    public void Save()
    {
        Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true); Changed?.Invoke();
    }
}
