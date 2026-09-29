using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DMGLauncher;

public record World(string Url, string Status, List<string> Players)
{
    public string Name => Status != "" ? Status : Url["byond://".Length..];
}

// Everything BYOND-related. Joining always goes through BYOND's own byond:// handler,
// so the pager's normal "Loading the game" / membership screens are never skipped.
public static class Byond
{
    public const string HubText = "https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis?format=text";
    public const string Banner = "https://www.byond.com/games/banners/86639.png";
    public const string JoinPage = "https://secure.byond.com/Join";
    public const int MaxLaunchTries = 3;

    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static Byond() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"DMG-Launcher/{Updater.CurrentText}");

    public static string? PagerExe =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Dantom\BYOND", "installpath", null) is string dir
        && File.Exists(Path.Combine(dir, "bin", "byond.exe")) ? Path.Combine(dir, "bin", "byond.exe") : null;

    public static bool IsRunning
    {
        get
        {
            var procs = Process.GetProcessesByName("byond");
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }
    }

    // Key the pager is signed in as: first line of HKCU\Software\Dantom\BYOND\key-info. Nothing else is read.
    public static string? SignedInKey
    {
        get
        {
            var first = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Dantom\BYOND", "key-info", null) switch
            {
                string[] lines => lines.FirstOrDefault(),
                string s => s.Split('\n')[0],
                _ => null,
            };
            first = first?.Trim();
            return string.IsNullOrEmpty(first) || first.StartsWith("Guest", StringComparison.OrdinalIgnoreCase) ? null : first;
        }
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public static void ShowPager() { if (PagerExe is { } exe) Process.Start(exe)?.Dispose(); }

    public static async Task<bool> EnsureRunning()
    {
        for (var i = 0; i < MaxLaunchTries && !IsRunning; i++)
        {
            try { ShowPager(); } catch (Exception ex) { Trace.WriteLine($"byond.exe start failed: {ex.Message}"); }
            for (var s = 0; s < 10 && !IsRunning; s++) await Task.Delay(1000);
        }
        return IsRunning;
    }

    // Downloads the current official installer and runs it normally (Windows shows its own UAC prompt).
    public static async Task Install()
    {
        var ver = (await Http.GetStringAsync("https://www.byond.com/download/version.txt")).Trim();
        if (!Regex.IsMatch(ver, @"^\d+\.\d+$")) throw new InvalidDataException($"unexpected BYOND version '{ver}'");
        var file = Path.Combine(Path.GetTempPath(), $"{ver}_byond.exe");
        await File.WriteAllBytesAsync(file, await Http.GetByteArrayAsync($"https://www.byond.com/download/build/{ver.Split('.')[0]}/{ver}_byond.exe"));
        using var p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        if (p != null) await p.WaitForExitAsync();
    }

    public static async Task<List<World>> FetchWorlds() => ParseWorlds(await Http.GetStringAsync(HubText));

    public static List<World> ParseWorlds(string hubText) =>
        Regex.Split(hubText, @"^world/\d+\s*$", RegexOptions.Multiline).Skip(1)
            .Select(b => new World(Field(b, "url"), Regex.Replace(Field(b, "status"), "<[^>]*>", "").Trim(), Players(b)))
            .Where(w => IsJoinable(w.Url))
            .ToList();

    static List<string> Players(string block) =>
        Regex.Match(block, @"^\s*players = list\((.*)\)\s*$", RegexOptions.Multiline) is { Success: true } m
            ? Regex.Matches(m.Groups[1].Value, Quoted).Select(x => Unescape(x.Groups[1].Value)).ToList()
            : [];

    const string Quoted = @"""((?:[^""\\]|\\.)*)""";
    static string Field(string block, string name) =>
        Regex.Match(block, $@"^\s*{name} = {Quoted}", RegexOptions.Multiline) is { Success: true } m ? Unescape(m.Groups[1].Value) : "";
    static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    // Hub data is untrusted: only plain byond://host[:port] addresses get handed to the shell.
    public static bool IsJoinable(string url) => Regex.IsMatch(url, @"^byond://[A-Za-z0-9.\-]+(:\d{1,5})?/?$");

    public static void Join(World w) { if (IsJoinable(w.Url)) OpenUrl(w.Url); }

    public static string Ckey(string key) => new(key.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    // Real key spelling from the member page, or null if the key doesn't exist (BYOND returns an HTML page then).
    public static async Task<string?> LookupKey(string key)
    {
        var k = Field(await Http.GetStringAsync($"https://www.byond.com/members/{Ckey(key)}?format=text"), "key");
        return k == "" ? null : k;
    }
}
