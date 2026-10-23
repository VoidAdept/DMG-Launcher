using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DMGLauncher;

// The launcher's own copy of the official BYOND client, downloaded from byond.com (approach from the MIT-licensed
// SS13 Launcher). Nothing here is modified: these are BYOND's own files, trimmed to the ones needed to play.
public static class ManagedByond
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMG-Launcher", "byond");
    // BYOND's own browser windows get a private profile so they never clash with an installed BYOND.
    static readonly string WebView2Data = Path.Combine(Root, "webview2_data");

    static readonly string[] Keep = ["dreamseeker.exe", "byond.exe", "byondcore.dll", "byondext.dll", "byondwin.dll", "WebView2Loader.dll", "fmodex.dll"];

    static string Bin(string version) => Path.Combine(Root, version, "byond", "bin");
    public static string? InstalledVersion =>
        Directory.Exists(Root) ? Directory.GetDirectories(Root).Select(Path.GetFileName)
            .Where(v => v != null && IsVersion(v) && File.Exists(Path.Combine(Bin(v), "dreamseeker.exe")))
            .OrderByDescending(v => Version.Parse(v!)).FirstOrDefault() : null;

    public static bool IsVersion(string v) => Regex.IsMatch(v, @"^\d+\.\d+$");
    static bool Installed(string version) => File.Exists(Path.Combine(Bin(version), "dreamseeker.exe"));

    // The latest public release, as BYOND's own download page advertises it.
    public static async Task<string> LatestVersion()
    {
        var v = (await Byond.Http.GetStringAsync("https://www.byond.com/download/version.txt")).Trim();
        return IsVersion(v) ? v : throw new InvalidDataException($"unexpected BYOND version '{v}'");
    }

    // Every build in BYOND's archive (byond.com/download/build/<major>/) for the newest `majors` major versions, newest
    // first. The archive rate-limits quick requests, so the folders are fetched one at a time with a pause between.
    public static async Task<List<string>> ArchiveVersions(int majors = 2)
    {
        var top = int.Parse((await LatestVersion()).Split('.')[0]);
        var found = new List<string>();
        for (var m = top; m > top - majors; m--)
        {
            if (m != top) await Task.Delay(1500);
            var html = await Byond.Http.GetStringAsync($"https://www.byond.com/download/build/{m}/");
            found.AddRange(Regex.Matches(html, @"href=""(\d+\.\d+)_byond\.zip""").Select(x => x.Groups[1].Value));
        }
        return found.Distinct().OrderByDescending(Version.Parse).ToList();
    }

    // Picks and installs the BYOND build to use: a version the player chose in Options, else the latest public release
    // (checked at launch unless the player turned that off). Keeps working offline with what is installed.
    public static async Task<string> Prepare(string? chosen, bool checkForUpdates, IProgress<string>? progress = null)
    {
        if (chosen != null && IsVersion(chosen)) return await Install(chosen, progress);
        if (!checkForUpdates && InstalledVersion is { } have) return have;
        string latest;
        try { latest = await LatestVersion(); }
        catch (Exception) when (InstalledVersion is { } offline) { return offline; }
        return await Install(latest, progress);
    }

    // Deletes the files of `version` so the next Prepare downloads it again (Options' "Reinstall").
    public static void Remove(string version)
    {
        if (IsVersion(version) && Directory.Exists(Path.Combine(Root, version))) Directory.Delete(Path.Combine(Root, version), true);
    }

    // Downloads one official build from byond.com (or the community mirror) unless it's already present.
    public static async Task<string> Install(string version, IProgress<string>? progress = null)
    {
        if (!IsVersion(version)) throw new ArgumentException($"not a BYOND version: {version}");
        if (Installed(version)) { Cleanup(version); return version; }

        progress?.Report($"Downloading BYOND {version}…");
        var major = version.Split('.')[0];
        byte[] zip;
        try { zip = await Byond.Http.GetByteArrayAsync($"https://www.byond.com/download/build/{major}/{version}_byond.zip"); }
        catch (HttpRequestException) { zip = await Byond.Http.GetByteArrayAsync($"https://byond-builds.dm-lang.org/{major}/{version}_byond.zip"); }

        progress?.Report($"Installing BYOND {version}…");
        var dir = Path.Combine(Root, version);
        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        using (var archive = new ZipArchive(new MemoryStream(zip)))
            foreach (var e in archive.Entries)
            {
                // Only byond/bin/<kept file> and byond/directx/* ; nothing else is written.
                var parts = e.FullName.Replace('\\', '/').Split('/');
                var wanted = parts.Length == 3 && parts[0] == "byond" &&
                             (parts[1] == "bin" && Keep.Contains(parts[2], StringComparer.OrdinalIgnoreCase) || parts[1] == "directx" && parts[2] != "");
                if (!wanted) continue;
                var target = Path.Combine(tmp, parts[0], parts[1], parts[2]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }
        if (!File.Exists(Path.Combine(tmp, "byond", "bin", "dreamseeker.exe"))) throw new InvalidDataException("the BYOND download has no dreamseeker.exe");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(tmp, dir);
        Cleanup(version);
        return version;
    }

    static void Cleanup(string keep)
    {
        foreach (var d in Directory.GetDirectories(Root))
            if (Path.GetFileName(d) is { } v && v != keep && (IsVersion(v) || v.EndsWith(".tmp")))
                try { Directory.Delete(d, true); } catch (IOException) { } // ponytail: still in use; retried next start
    }

    static ProcessStartInfo Start(string exe, string arg)
    {
        var psi = new ProcessStartInfo(exe, $"\"{arg}\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.Environment["WEBVIEW2_USER_DATA_FOLDER"] = WebView2Data;
        return psi;
    }

    // BYOND's pager runs as a single instance, so an installed BYOND that's already open would take the join over.
    // A process whose path can't be read is running elevated, which the launcher's own copy never does.
    public static bool OtherByondRunning()
    {
        var procs = Process.GetProcessesByName("byond").Concat(Process.GetProcessesByName("dreamseeker")).ToArray();
        try
        {
            return procs.Any(p =>
            {
                try { return p.MainModule?.FileName is not { } f || !f.StartsWith(Root, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return true; }
            });
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    // DreamSeeker's own windows before the game: "Loading game information...", "BYOND: Your Game Is Starting".
    static bool IsGameTitle(string title) => title.Length > 0 && !title.StartsWith("BYOND") && !title.StartsWith("Loading");

    // Join by starting the launcher's DreamSeeker with the world address (plus the web session id from DMG's Play link).
    // DreamSeeker brings up BYOND's pager itself (minimised), signs in, and runs BYOND's normal "Your Game Is Starting"
    // screen, including the non-member wait. Only once the game window is open is the pager closed, so the wait always
    // runs to completion and nothing of BYOND's is modified or skipped.
    // Returns null once the game window is open, otherwise what went wrong.
    public static async Task<string?> Connect(string version, string worldUrl, string webId, IProgress<string>? progress = null)
    {
        if (!Byond.IsJoinable(worldUrl)) throw new ArgumentException($"not a BYOND world address: {worldUrl}");
        if (!Regex.IsMatch(webId, "^[0-9a-fA-F]+$")) throw new ArgumentException("invalid BYOND web session id");

        Directory.CreateDirectory(WebView2Data);
        using var seeker = Process.Start(Start(Path.Combine(Bin(version), "dreamseeker.exe"), $"{worldUrl.TrimEnd('/')}##webid={webId}"))
                           ?? throw new InvalidOperationException("DreamSeeker did not start");
        progress?.Report("BYOND is starting your game…");

        // The non-member wait comes first, so allow well beyond it.
        var opened = false;
        for (var i = 0; i < 180 && !opened; i++)
        {
            await Task.Delay(1000);
            seeker.Refresh();
            if (seeker.HasExited) return "BYOND closed before the game opened.";
            opened = IsGameTitle(seeker.MainWindowTitle);
        }
        if (!opened) return "The game did not open within 3 minutes.";

        progress?.Report("Game opened, finishing sign-in…");
        await Task.Delay(5000);
        ClosePager();
        return null;
    }

    // Closes only the launcher's own pager (an installed BYOND is never touched).
    static void ClosePager()
    {
        foreach (var p in Process.GetProcessesByName("byond"))
            using (p)
                try { if (p.MainModule?.FileName.StartsWith(Root, StringComparison.OrdinalIgnoreCase) == true) p.Kill(); }
                catch (Exception) { } // elevated = not ours
    }
}
