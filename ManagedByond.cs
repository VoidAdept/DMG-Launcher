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
        var running = Own("dreamseeker");
        if (running.Length == 0) PurgeWebCaches(WebView2Data);
        foreach (var p in running) p.Dispose();
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

    // DreamSeeker needs Microsoft's DirectX 9 extensions (d3dx9_43.dll), which Windows doesn't include: without them it
    // quits at once. BYOND's own installer runs the DirectX setup it ships in byond\directx; this runs that same setup.
    public static bool DirectXMissing => !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "d3dx9_43.dll"));
    public static async Task<bool> InstallDirectX(string version)
    {
        var setup = Path.Combine(Root, version, "byond", "directx", "DXSETUP.exe");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(setup, "/silent") { UseShellExecute = true, Verb = "runas" })!;
            await p.WaitForExitAsync();
            ByondWeb.Log($"DirectX setup exit code {p.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception e) { ByondWeb.Log($"DirectX setup not run: {e.Message}"); } // UAC declined
        return !DirectXMissing;
    }

    // WebView2 (BYOND's own browser panes, and the launcher's sign-in) keeps disk caches that grow to tens of MB.
    // They only speed up repeat page loads, so they're dropped at startup while nothing is using them.
    static readonly string[] CacheDirs = ["Cache", "Code Cache", "GPUCache", "GrShaderCache", "GraphiteDawnCache", "DawnCache", "DawnWebGPUCache", "DawnGraphiteCache", "ShaderCache", "CacheStorage", "ScriptCache", "Crashpad"];
    // Add-ons Edge's component updater fetches in the background (DRM, ad-block lists, speech, hyphenation): ~58 MB that
    // a login page never uses. Only dropped where updates are switched off, or they'd be fetched again every session.
    static readonly string[] ComponentDirs = ["WidevineCdm", "component_crx_cache", "Subresource Filter", "Speech Recognition", "hyphen-data"];
    public static void PurgeWebCaches(string dataDir, bool components = false)
    {
        if (!Directory.Exists(dataDir)) return;
        foreach (var d in Directory.EnumerateDirectories(dataDir, "*", SearchOption.AllDirectories)
                     .Where(d => CacheDirs.Contains(Path.GetFileName(d)) || components && ComponentDirs.Contains(Path.GetFileName(d))).ToList())
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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

    // The launcher's own copies of a BYOND process (a process whose path can't be read is elevated, so not ours).
    static Process[] Own(string name) => Process.GetProcessesByName(name).Where(p =>
    {
        try { if (p.MainModule?.FileName?.StartsWith(Root, StringComparison.OrdinalIgnoreCase) == true) return true; }
        catch (Exception) { }
        p.Dispose();
        return false;
    }).ToArray();

    // BYOND's windows before the game: "Loading game information...", "BYOND: Your Game Is Starting".
    static bool IsGameTitle(string title) => title.Length > 0 && !title.StartsWith("BYOND") && !title.StartsWith("Loading");

    public const string ClosedEarly = "BYOND closed before the game opened. If BYOND showed a message, it says why.";

    // Two ways in, both BYOND's own:
    // - With the web session id from DMG's Play link: the launcher's DreamSeeker gets the world address plus that id and
    //   signs in from it, quietly. Where BYOND's pager signs itself in with a saved login, that login can end the
    //   byond.com session, so the id arrives dead and DreamSeeker joins as a guest, which DMG refuses.
    // - Without (webId null): the world address goes to the launcher's pager (byond.exe), the same thing a byond:// link
    //   does. It signs in with its saved login, or shows BYOND's own Login window for the player, then opens the game.
    // Either way BYOND runs its normal "Your Game Is Starting" screen, including the non-member wait. Only once the game
    // window is open is the pager closed, so nothing of BYOND's is skipped.
    // Returns null once the game window is open, otherwise what went wrong.
    public static async Task<string?> Connect(string version, string worldUrl, string? webId, IProgress<string>? progress = null)
    {
        if (!Byond.IsJoinable(worldUrl)) throw new ArgumentException($"not a BYOND world address: {worldUrl}");
        if (webId != null && !Regex.IsMatch(webId, "^[0-9a-fA-F]+$")) throw new ArgumentException("invalid BYOND web session id");

        Directory.CreateDirectory(WebView2Data);
        var (exe, address) = webId == null
            ? ("byond.exe", worldUrl.TrimEnd('/'))
            : ("dreamseeker.exe", $"{worldUrl.TrimEnd('/')}##webid={webId}");
        ByondWeb.Log(webId == null ? "Joining through BYOND's pager and its own sign-in" : "Joining with the web session");
        using var started = Process.Start(Start(Path.Combine(Bin(version), exe), address))
                            ?? throw new InvalidOperationException("BYOND did not start");
        progress?.Report(webId == null ? "BYOND is starting your game (sign in there if BYOND asks)…" : "BYOND is starting your game…");

        // The non-member wait (or BYOND's Login window) comes first, so allow well beyond it. Every DreamSeeker of the
        // launcher's copy is watched and each change logged. It's a failure once DreamSeeker has come and gone, or
        // nothing of BYOND's is left running.
        var opened = false;
        var exitLogged = false;
        var seen = false;
        var goneFor = 0;
        string? last = null;
        for (var i = 0; i < 180 && !opened; i++)
        {
            await Task.Delay(1000);
            started.Refresh();
            if (started.HasExited && !exitLogged)
            {
                exitLogged = true;
                ByondWeb.Log($"{exe} {started.Id} exited, code 0x{started.ExitCode:X8}");
                if (started.ExitCode == unchecked((int)0xC0000135)) // STATUS_DLL_NOT_FOUND
                    return "BYOND couldn't start because a Windows component it needs is missing (error 0xC0000135).";
            }
            var seekers = Own("dreamseeker");
            var pagers = Own("byond");
            try
            {
                var state = $"DreamSeekers: {(seekers.Length == 0 ? "none" : string.Join(", ", seekers.Select(p => $"{p.Id} \"{p.MainWindowTitle}\"")))}; " +
                            $"pager {(pagers.Length > 0 ? "running" : "not running")}";
                if (state != last) ByondWeb.Log(state);
                last = state;
                opened = seekers.Any(p => IsGameTitle(p.MainWindowTitle));
                seen |= seekers.Length > 0;
                goneFor = seekers.Length == 0 && (seen || pagers.Length == 0) ? goneFor + 1 : 0;
            }
            finally { foreach (var p in seekers.Concat(pagers)) p.Dispose(); }
            if (goneFor >= 10) return ClosedEarly;
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
        foreach (var p in Own("byond"))
            using (p)
                try { p.Kill(); } catch (Exception) { } // already gone
    }
}
