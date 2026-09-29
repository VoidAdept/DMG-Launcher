using System.Text.Json;

namespace DMGLauncher;

// %APPDATA%\DMG-Launcher\settings.json. Holds no credentials: login is the BYOND pager's.
public class Settings
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMG-Launcher");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public List<string> Friends { get; set; } = [];
    public bool CheckUpdatesOnStart { get; set; } = true;

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (JsonException) { File.Copy(FilePath, FilePath + ".bad", true); return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void ClearAll() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
}
