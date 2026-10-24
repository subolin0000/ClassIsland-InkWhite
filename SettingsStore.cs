using System.Text.Json;
namespace AdaptiveText;
public sealed class Options
{
    public int FormatVersion { get; set; }
    public int ClassroomStableMs { get; set; } = 3000;
    public int ClassroomFadeMs { get; set; } = 2000;
    public bool Enabled { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;
    public bool Shadow { get; set; } = false;
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
        try { Value = File.Exists(path) ? JsonSerializer.Deserialize<Options>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { Value = new(); LoadError = e.Message; }
        Normalize();
    }
    private void Normalize()
    {
        // Earlier classroom versions ignored Shadow. Do not accidentally enable a new effect on upgrade.
        if (Value.FormatVersion < 1) { Value.Shadow = false; Value.FormatVersion = 1; }
        Value.ClassroomStableMs = Math.Clamp(Value.ClassroomStableMs, 1500, 30000);
        Value.ClassroomFadeMs = Math.Clamp(Value.ClassroomFadeMs, 500, 10000);
        Value.IntervalMs = Math.Clamp(Value.IntervalMs, 1000, 2000);
    }
    public void Save()
    {
        Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true); Changed?.Invoke();
    }
}
