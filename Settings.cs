using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMGLauncher;

// A BYOND key the launcher keeps a byond.com login for. The login itself (BYOND's cookie) lives in the browser
// profile `Profile`; this record only names it and dates it.
public class StoredKey
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    public string Key { get; set; } = "";
    public string Profile { get; set; } = "";
    public DateTime SignedInUtc { get; set; }
    [JsonIgnore] public bool Expired => DateTime.UtcNow - SignedInUtc > Lifetime;
    [JsonIgnore] public int DaysLeft => Math.Max(0, (int)Math.Ceiling((SignedInUtc + Lifetime - DateTime.UtcNow).TotalDays));
}

// %APPDATA%\DMG-Launcher\settings.json. Holds no passwords: BYOND logins are BYOND's own cookies (see StoredKey).
public class Settings
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMG-Launcher");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public List<string> Friends { get; set; } = [];
    public bool CheckUpdatesOnStart { get; set; } = true;
    public bool CheckByondUpdatesOnStart { get; set; } = true;
    public string? ByondVersion { get; set; } // null = latest public release
    public List<StoredKey> Keys { get; set; } = [];
    public string? LastKey { get; set; }

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
