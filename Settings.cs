using System.Security.Cryptography;
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
    [JsonIgnore] public List<StoredKey> Keys { get; set; } = [];
    [JsonIgnore] public string? LastKey { get; set; }

    // Keys and LastKey are written only as this DPAPI blob (Windows user account bound): another account or machine
    // can't read it, and it fails closed to "no stored logins".
    internal record Logins(List<StoredKey> Keys, string? LastKey);
    static readonly byte[] Entropy = "DMG-Launcher logins v1"u8.ToArray();
    public string? StoredLogins
    {
        get => Keys.Count == 0 && LastKey == null ? null : Convert.ToBase64String(ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(new Logins(Keys, LastKey), Json.Default.Logins), Entropy, DataProtectionScope.CurrentUser));
        set
        {
            try
            {
                var l = value == null ? null : JsonSerializer.Deserialize(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser), Json.Default.Logins);
                (Keys, LastKey) = (l?.Keys ?? [], l?.LastKey);
            }
            catch (Exception e) when (e is CryptographicException or FormatException or JsonException) { (Keys, LastKey) = ([], null); }
        }
    }

    // Read once from settings written before encryption, then saved encrypted.
    [JsonPropertyName("Keys"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StoredKey>? PlainKeys { get => null; set { if (value != null) Keys = value; } }
    [JsonPropertyName("LastKey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlainLastKey { get => null; set { if (value != null) LastKey = value; } }

    public static Settings Load()
    {
        try
        {
            var text = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize(text, Json.Default.Settings) ?? new();
            if (text.Contains("\"Keys\"")) s.Save(); // re-save older plain-text logins encrypted
            return s;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (JsonException) { File.Copy(FilePath, FilePath + ".bad", true); return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Default.Settings));
    }

    public static void ClearAll() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
}

// Source-generated JSON, so the trimmed single-file build needs no reflection.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Settings.Logins))]
[JsonSerializable(typeof(ByondWeb.PageState))]
partial class Json : JsonSerializerContext;
