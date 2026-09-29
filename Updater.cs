using System.Diagnostics;
using System.Text.Json;

namespace DMGLauncher;

public static class Updater
{
    const string LatestRelease = "https://api.github.com/repos/VoidAdept/DMG-Launcher/releases/latest";
    const string AssetName = "DMG-Launcher.exe";

    public record Release(Version Version, string ExeUrl);

    public static Version Current => Norm(typeof(Updater).Assembly.GetName().Version!);
    public static string CurrentText => Current.ToString(3);

    // 0.1.0 and 0.1.0.0 must compare equal, so everything is cut to major.minor.build.
    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static bool IsNewer(string tag, Version current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var v) && Norm(v) > Norm(current);

    // null = up to date. Throws if GitHub can't be reached (or the repo is private).
    public static async Task<Release?> Check()
    {
        using var doc = JsonDocument.Parse(await Byond.Http.GetStringAsync(LatestRelease));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!IsNewer(tag, Current)) return null;
        var url = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == AssetName)
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();
        return url is null ? null : new Release(Norm(Version.Parse(tag.TrimStart('v', 'V'))), url);
    }

    // Windows lets a running exe be renamed, so: download .new, move self to .old, move .new in, restart.
    public static async Task Apply(Release r)
    {
        var exe = Environment.ProcessPath!;
        await File.WriteAllBytesAsync(exe + ".new", await Byond.Http.GetByteArrayAsync(r.ExeUrl));
        File.Move(exe, exe + ".old", true);
        File.Move(exe + ".new", exe);
        Process.Start(exe)?.Dispose();
        Environment.Exit(0);
    }

    public static void CleanupOld()
    {
        try { File.Delete(Environment.ProcessPath + ".old"); }
        catch (IOException) { } // ponytail: old process still exiting; retried next start
        catch (UnauthorizedAccessException) { }
    }
}
