using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Microsoft.Web.WebView2.Core;

namespace DMGLauncher;

// BYOND sign-in through BYOND's own website, shown in a WebView2 inside the launcher (approach from the MIT-licensed
// SS13 Launcher). The player types their password into byond.com itself; the launcher never sees or stores it.
// "Remember me" is BYOND's own cookie in a private browser profile.
public sealed class ByondWeb
{
    const string HubPage = "https://www.byond.com/games/EternalDuelistSoul/DuelMonstersGenesis";
    const string LoginPage = "https://secure.byond.com/login.cgi";
    const string LogoutPage = "https://secure.byond.com/login.cgi?login=0";
    static readonly string Profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMG-Launcher", "webview");

    // BYOND only shows the hub's Play link (which carries the session id) to Windows clients; Cloudflare's
    // "Just a moment" check is waited out on every page.
    const string ReadPage = """
        (() => {
          if ((document.title || '').toLowerCase().includes('just a moment')) return null;
          const n = document.querySelector('.topbar_name_link');
          const j = document.querySelector('.join_link');
          const links = [...document.querySelectorAll('a[href*="webid"], a[href^="byond:"], a.join_link')]
            .slice(0, 6).map(a => (a.className || '-') + ' ' + a.getAttribute('href'));
          return { key: n ? n.textContent.trim().split('\n')[0].trim() : null, href: j ? j.getAttribute('href') : null,
                   ready: document.readyState === 'complete', url: location.href, title: document.title, links };
        })()
        """;

    public record Session(string? Key, string? WebId, string? PlayLink)
    {
        public bool SignedIn => Key != null && WebId != null && !WebId.Equals("guest", StringComparison.OrdinalIgnoreCase);
    }
    record PageState(string? key, string? href, bool ready, string? url, string? title, string[]? links);

    readonly Window _owner;
    CoreWebView2Controller? _view;
    string? _lastRead;

    // ponytail: plain append-only diagnostics; no rotation, it only grows while debugging sign-in
    static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMG-Launcher", "launcher.log");
    public static void Log(string line)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!); File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    public ByondWeb(Window owner) => _owner = owner;

    async Task<CoreWebView2Controller> View()
    {
        if (_view != null) return _view;
        var hwnd = _owner.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("no window handle");
        var env = await CoreWebView2Environment.CreateAsync(null, Profile);
        _view = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _view.IsVisible = false;
        var s = _view.CoreWebView2.Settings;
        s.UserAgent = $"DMG-Launcher/{Updater.CurrentText} (Windows)";
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        // Links that open new windows (BYOND's register / forgot password) go to the real browser instead.
        _view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Byond.OpenUrl(e.Uri); };
        return _view;
    }

    async Task<PageState?> Read(CoreWebView2Controller view)
    {
        var json = await view.CoreWebView2.ExecuteScriptAsync(ReadPage);
        if (json != _lastRead) { Log($"page: {Regex.Replace(json, "webid=[0-9a-fA-F]+", "webid=<hidden>")}"); _lastRead = json; }
        return json == "null" ? null : JsonSerializer.Deserialize<PageState>(json);
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

    // Loads DMG's hub page out of sight and reads who is signed in and the session id from its Play link.
    public async Task<Session> CheckSession()
    {
        var view = await View();
        view.IsVisible = false;
        await NavigateAndWait(view.CoreWebView2, HubPage);
        for (var i = 0; i < 60; i++) // ~30 s, Cloudflare included
        {
            // Only trust the hub page itself: a page still showing from before (e.g. a ##guest Play link from an
            // earlier check) must not be read as the answer.
            if (await Read(view) is { ready: true } p && p.url?.StartsWith(HubPage, StringComparison.OrdinalIgnoreCase) == true
                && (p.href != null || i > 10))
                return new Session(p.key, WebIdFrom(p.href), p.href);
            await Task.Delay(500);
        }
        return new Session(null, null, null);
    }

    // Shows BYOND's login page over the launcher at `bounds` (physical pixels) until the player is signed in or cancels.
    public async Task<bool> SignIn(System.Drawing.Rectangle bounds, CancellationToken cancel)
    {
        var view = await View();
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

    public async Task SignOut()
    {
        var view = await View();
        view.IsVisible = false;
        await NavigateAndWait(view.CoreWebView2, LogoutPage);
    }

    public void Close() => _view?.Close();
}
