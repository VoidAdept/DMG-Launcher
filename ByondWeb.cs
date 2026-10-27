using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Microsoft.Web.WebView2.Core;

namespace DMGLauncher;

// BYOND sign-in through BYOND's own website, shown in a WebView2 inside the launcher (approach from the MIT-licensed
// SS13 Launcher). The player types their password into byond.com itself; the launcher never sees or stores it.
// Each stored BYOND key is BYOND's own login cookie in its own private browser profile, so several keys can be kept
// side by side and any one of them can be forgotten without touching the others.
public sealed class ByondWeb
{
    const string HubPage = "https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis";
    const string LoginPage = "https://secure.byond.com/login.cgi";
    const string LogoutPage = "https://secure.byond.com/login.cgi?login=0";
    static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMG-Launcher");
    static readonly string Profiles = Path.Combine(Data, "logins");

    // BYOND only shows the hub's Play link (which carries the session id) to Windows clients; Cloudflare's
    // "Just a moment" check is waited out on every page.
    const string ReadPage = """
        (() => {
          if ((document.title || '').toLowerCase().includes('just a moment')) return null;
          const n = document.querySelector('.topbar_name_link');
          const j = document.querySelector('.join_link');
          return { key: n ? n.textContent.trim().split('\n')[0].trim() : null, href: j ? j.getAttribute('href') : null,
                   ready: document.readyState === 'complete', url: location.href };
        })()
        """;

    public record Session(string? Key, string? WebId)
    {
        public static readonly Session None = new(null, null);
        public bool SignedIn => Key != null && WebId != null && !WebId.Equals("guest", StringComparison.OrdinalIgnoreCase);
    }
    internal record PageState(string? key, string? href, bool ready, string? url);

    readonly Window _owner;
    CoreWebView2Environment? _env;
    CoreWebView2Controller? _view;
    string? _profile;
    string? _lastRead;

    // ponytail: plain append-only diagnostics; no rotation, it only grows while debugging sign-in
    static readonly string LogFile = Path.Combine(Data, "launcher.log");
    public static void Log(string line)
    {
        try { Directory.CreateDirectory(Data); File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    public ByondWeb(Window owner)
    {
        _owner = owner;
        // The single-profile prototype kept its login in "webview"; that login was never subject to the 30-day rule.
        try { if (Directory.Exists(Path.Combine(Data, "webview"))) Directory.Delete(Path.Combine(Data, "webview"), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ManagedByond.PurgeWebCaches(Profiles, components: true); // before this session's browser starts; cookies (the logins) stay
    }

    public static string NewProfile() => "k" + Guid.NewGuid().ToString("N");

    async Task<CoreWebView2Controller> View(string profile)
    {
        if (_view != null && _profile == profile) return _view;
        _view?.Close();
        _view = null;
        var hwnd = _owner.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("no window handle");
        _env ??= await CoreWebView2Environment.CreateAsync(null, Profiles, new CoreWebView2EnvironmentOptions("--disable-component-update"));
        var options = _env.CreateCoreWebView2ControllerOptions();
        options.ProfileName = profile;
        var view = await _env.CreateCoreWebView2ControllerAsync(hwnd, options);
        view.IsVisible = false;
        var s = view.CoreWebView2.Settings;
        s.UserAgent = $"DMG-Launcher/{Updater.CurrentText} (Windows)";
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        // Links that open new windows (BYOND's register / forgot password) go to the real browser instead.
        view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Byond.OpenUrl(e.Uri); };
        (_view, _profile) = (view, profile);
        return view;
    }

    async Task<PageState?> Read(CoreWebView2Controller view)
    {
        var json = await view.CoreWebView2.ExecuteScriptAsync(ReadPage);
        if (json != _lastRead)
        {
            var masked = Regex.Replace(json, "webid=[0-9a-fA-F]+", "webid=<hidden>");
            Log($"page: {Regex.Replace(masked, "\"key\":\"[^\"]*\"", "\"key\":\"<hidden>\"")}");
            _lastRead = json;
        }
        return json == "null" ? null : JsonSerializer.Deserialize(json, Json.Default.PageState);
    }

    static async Task NavigateAndWait(CoreWebView2 web, string url)
    {
        var done = new TaskCompletionSource();
        void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();
        web.NavigationCompleted += OnDone;
        web.Navigate(url);
        await Task.WhenAny(done.Task, Task.Delay(20000));
        web.NavigationCompleted -= OnDone;
    }

    static string? WebIdFrom(string? href) => href is null ? null : Regex.Match(href, @"webid=([0-9a-fA-F]+|guest)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    // Loads DMG's hub page out of sight in `profile` and reads who is signed in and the session id from its Play link.
    public async Task<Session> CheckSession(string profile)
    {
        var view = await View(profile);
        view.IsVisible = false;
        await NavigateAndWait(view.CoreWebView2, HubPage);
        for (var i = 0; i < 60; i++) // ~30 s, Cloudflare included
        {
            // Only trust the hub page itself: a page still showing from before (e.g. a ##guest Play link from an
            // earlier check) must not be read as the answer.
            if (await Read(view) is { ready: true } p && p.url?.StartsWith(HubPage, StringComparison.OrdinalIgnoreCase) == true
                && (p.href != null || i > 10))
                return new Session(p.key, WebIdFrom(p.href));
            await Task.Delay(500);
        }
        return Session.None;
    }

    // Shows BYOND's login page (in `profile`) over the launcher at `bounds` (physical pixels) until the player is
    // signed in or cancels.
    public async Task<bool> SignIn(string profile, System.Drawing.Rectangle bounds, CancellationToken cancel)
    {
        var view = await View(profile);
        view.Bounds = bounds;
        view.CoreWebView2.Navigate(LoginPage);
        view.IsVisible = true;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                await Task.Delay(500);
                if (await Read(view) is { key: not null }) return true;
            }
            return false;
        }
        finally { view.IsVisible = false; }
    }

    // Signs the profile out on byond.com, wipes everything it stored and deletes it.
    public async Task Forget(string profile)
    {
        var view = await View(profile);
        view.IsVisible = false;
        await NavigateAndWait(view.CoreWebView2, LogoutPage);
        await view.CoreWebView2.Profile.ClearBrowsingDataAsync();
        view.CoreWebView2.Profile.Delete(); // closes this view; its folder goes on the next browser start
        (_view, _profile) = (null, null);
    }

    public void Close() => _view?.Close();
}
