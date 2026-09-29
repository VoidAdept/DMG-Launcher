using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DMGLauncher;

public partial class MainWindow : Window
{
    Settings _settings = Settings.Load();
    List<World> _worlds = [];
    readonly DispatcherTimer _loginTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    TaskCompletionSource<bool>? _ask;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Launcher version {Updater.CurrentText}";
        UpdatesOnStart.IsChecked = _settings.CheckUpdatesOnStart;
        _loginTimer.Tick += (_, _) => UpdateLogin();
        _serverTimer.Tick += async (_, _) => await RefreshWorlds();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LoadBanner();

        if (_settings.CheckUpdatesOnStart)
        {
            StartupText.Text = "Checking for launcher updates…";
            await CheckForUpdate();
        }

        StartupText.Text = "Checking for BYOND…";
        if (Byond.PagerExe is null)
        {
            if (!await Ask("BYOND is required to play Duel Monsters Genesis, but it isn't installed.\n\nDownload and install it now?", "Install", "Quit"))
            {
                await Fatal("BYOND is required. The launcher will now close.");
                return;
            }
            StartupText.Text = "Downloading BYOND… follow the installer's prompts.";
            try { await Byond.Install(); }
            catch (Exception ex) { await Fatal($"BYOND could not be installed: {ex.Message}"); return; }
            if (Byond.PagerExe is null) { await Fatal("BYOND still isn't installed. The launcher will now close."); return; }
        }

        StartupText.Text = "Starting BYOND…";
        if (!await Byond.EnsureRunning())
        {
            await Fatal($"BYOND could not be started after {Byond.MaxLaunchTries} tries. The launcher will now close.");
            return;
        }

        StartupPanel.IsVisible = false;
        LoginPanel.IsVisible = true;
        UpdateLogin();
        _loginTimer.Start();
    }

    async void LoadBanner()
    {
        try { Banner.Source = new Bitmap(new MemoryStream(await Byond.Http.GetByteArrayAsync(Byond.Banner))); }
        catch (Exception ex) { Trace.WriteLine($"banner: {ex.Message}"); } // ponytail: cosmetic only
    }

    // ---------- prompt overlay ----------

    Task<bool> Ask(string text, string yes, string? no = null)
    {
        AskText.Text = text;
        AskYes.Content = yes;
        AskNo.Content = no;
        AskNo.IsVisible = no != null;
        Overlay.IsVisible = true;
        _ask = new();
        return _ask.Task;
    }

    void Answer(bool yes) { Overlay.IsVisible = false; _ask?.TrySetResult(yes); }
    void AskYes_Click(object? s, RoutedEventArgs e) => Answer(true);
    void AskNo_Click(object? s, RoutedEventArgs e) => Answer(false);

    async Task Fatal(string message)
    {
        await Ask(message, "Quit");
        Close();
    }

    // ---------- login ----------

    void UpdateLogin()
    {
        var key = Byond.SignedInKey;
        LoginStatus.Text = key is null
            ? "BYOND isn't signed in. Sign in through the BYOND pager, then come back here."
            : $"Signed in to BYOND as {key}.";
        ContinueButton.IsEnabled = key is not null;
        SignInButton.Content = key is null ? "Sign in with BYOND" : "Switch account in BYOND";
    }

    void SignIn_Click(object? s, RoutedEventArgs e) => Byond.ShowPager();
    void CreateAccount_Click(object? s, RoutedEventArgs e) => Byond.OpenUrl(Byond.JoinPage);

    async void Continue_Click(object? s, RoutedEventArgs e)
    {
        var key = Byond.SignedInKey;
        if (key is null) { UpdateLogin(); return; }
        _loginTimer.Stop();
        HeaderKey.Text = $"Signed in as {key}";
        KeyText.Text = $"BYOND key: {key}";
        LoginPanel.IsVisible = false;
        MainTabs.IsVisible = true;
        RenderFriends();
        _serverTimer.Start();
        await RefreshWorlds();
    }

    // ---------- server list ----------

    async void Refresh_Click(object? s, RoutedEventArgs e) => await RefreshWorlds();

    async Task RefreshWorlds()
    {
        ServerStatus.Text = "Refreshing…";
        try
        {
            _worlds = await Byond.FetchWorlds();
            ServerStatus.Text = $"{_worlds.Count} server(s) online · updated {DateTime.Now:t}";
        }
        catch (Exception ex) { ServerStatus.Text = $"Couldn't reach the BYOND hub: {ex.Message}"; }

        ServerRows.Children.Clear();
        foreach (var w in _worlds)
            ServerRows.Children.Add(Row(w.Name, $"{w.Players.Count} player(s)", false, ("Join", () => Byond.Join(w))));
        if (_worlds.Count == 0)
            ServerRows.Children.Add(new TextBlock { Text = "No DMG servers are online right now.", Opacity = 0.7 });
        RenderFriends();
    }

    // ---------- friends ----------

    void RenderFriends()
    {
        FriendRows.Children.Clear();
        foreach (var friend in _settings.Friends)
        {
            var w = _worlds.FirstOrDefault(w => w.Players.Any(p => Byond.Ckey(p) == Byond.Ckey(friend)));
            var remove = ("Remove", (Action)(() => { _settings.Friends.Remove(friend); _settings.Save(); RenderFriends(); }));
            FriendRows.Children.Add(w is null
                ? Row(friend, "Not on DMG", false, remove)
                : Row(friend, $"On DMG — {w.Name}", true, ("Join", () => Byond.Join(w)), remove));
        }
        if (_settings.Friends.Count == 0)
            FriendRows.Children.Add(new TextBlock { Text = "No friends added yet.", Opacity = 0.7 });
    }

    void FriendInput_KeyDown(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) AddFriend_Click(s, e); }

    async void AddFriend_Click(object? s, RoutedEventArgs e)
    {
        var input = FriendInput.Text?.Trim() ?? "";
        if (Byond.Ckey(input) == "") return;
        if (_settings.Friends.Any(f => Byond.Ckey(f) == Byond.Ckey(input))) { FriendStatus.Text = "Already on your list."; return; }

        FriendStatus.Text = "Looking up key…";
        string? key;
        try { key = await Byond.LookupKey(input); }
        catch (Exception ex) { FriendStatus.Text = $"Lookup failed: {ex.Message}"; return; }
        if (key is null) { FriendStatus.Text = $"No BYOND key named \"{input}\"."; return; }

        _settings.Friends.Add(key);
        _settings.Save();
        FriendInput.Text = "";
        FriendStatus.Text = "";
        RenderFriends();
    }

    // ---------- options ----------

    async void CheckUpdates_Click(object? s, RoutedEventArgs e) => await CheckForUpdate();

    async Task CheckForUpdate()
    {
        UpdateStatus.Text = "Checking…";
        Updater.Release? r;
        try { r = await Updater.Check(); }
        catch (Exception ex)
        {
            Trace.WriteLine($"update check: {ex.Message}");
            UpdateStatus.Text = "Update check unavailable.";
            return;
        }
        if (r is null) { UpdateStatus.Text = "You're on the latest version."; return; }

        UpdateStatus.Text = $"Version {r.Version.ToString(3)} is available.";
        if (!await Ask($"DMG Launcher {r.Version.ToString(3)} is available (you have {Updater.CurrentText}).\n\nUpdate now?", "Update", "Skip"))
            return;
        UpdateStatus.Text = StartupText.Text = "Downloading update…";
        try { await Updater.Apply(r); }
        catch (Exception ex) { await Ask($"The update failed: {ex.Message}", "OK"); }
    }

    void UpdatesOnStart_Changed(object? s, RoutedEventArgs e)
    {
        var on = UpdatesOnStart.IsChecked == true;
        if (on == _settings.CheckUpdatesOnStart) return;
        _settings.CheckUpdatesOnStart = on;
        _settings.Save();
    }

    void ResetSettings_Click(object? s, RoutedEventArgs e)
    {
        _settings = new Settings { Friends = _settings.Friends };
        _settings.Save();
        UpdatesOnStart.IsChecked = _settings.CheckUpdatesOnStart;
        OptionsStatus.Text = "Settings reset to defaults (friends list kept).";
    }

    async void ClearData_Click(object? s, RoutedEventArgs e)
    {
        if (!await Ask("Delete all stored launcher data, including your friends list? The launcher will restart.", "Clear data", "Cancel"))
            return;
        try { Settings.ClearAll(); }
        catch (Exception ex) { OptionsStatus.Text = $"Couldn't clear data: {ex.Message}"; return; }
        Process.Start(Environment.ProcessPath!)?.Dispose();
        Close();
    }

    // ---------- shared row ----------

    static Border Row(string title, string detail, bool highlight, params (string Label, Action OnClick)[] buttons)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220,Auto") };
        grid.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var d = new TextBlock { Text = detail, VerticalAlignment = VerticalAlignment.Center, Opacity = highlight ? 1 : 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
        if (highlight) d.Foreground = Brushes.LightGreen;
        Grid.SetColumn(d, 1);
        grid.Children.Add(d);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, onClick) in buttons)
        {
            var b = new Button { Content = label };
            if (label == "Join") b.Classes.Add("accent");
            b.Click += (_, _) => onClick();
            bar.Children.Add(b);
        }
        Grid.SetColumn(bar, 2);
        grid.Children.Add(bar);
        return new Border { Background = new SolidColorBrush(Color.Parse("#1E1A26")), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10), Child = grid };
    }
}
