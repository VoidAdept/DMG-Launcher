using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DMGLauncher;

public record World(string Url, string Status, List<string> Players)
{
    public string Address => Url["byond://".Length..].TrimEnd('/');
    // The hub has no per-world name, only the status text the server publishes.
    public string Name => Status != "" ? $"{Status} · {Address}" : Address;
}

// Hub data and BYOND web helpers. Joining lives in ManagedByond (BYOND's own client, never modified).
public static class Byond
{
    public const string HubText = "https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis?format=text";
    public const string Banner = "https://www.byond.com/games/banners/86639.png";
    public const string JoinPage = "https://secure.byond.com/Join";

    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static Byond() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"DMG-Launcher/{Updater.CurrentText}");

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

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

    public static string Ckey(string key) => new(key.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    // Real key spelling from the member page, or null if the key doesn't exist (BYOND returns an HTML page then).
    public static async Task<string?> LookupKey(string key)
    {
        var k = Field(await Http.GetStringAsync($"https://www.byond.com/members/{Ckey(key)}?format=text"), "key");
        return k == "" ? null : k;
    }
}
