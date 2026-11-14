using System.IO;
using System.Text.Json;

namespace Recorder;
public sealed class Settings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Recorder");
    public string? Display { get; set; }
    public bool SystemAudio { get; set; } = true;
    public bool Cursor { get; set; } = true;
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Recorder");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DataFolder, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
