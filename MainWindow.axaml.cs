using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace DMGLauncher;

public partial class MainWindow : Window
{
    Settings _settings = Settings.Load();
    List<World> _worlds = [];
    readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly ByondWeb _web;
    ByondWeb.Session _session = new(null, null, null);
    string? _byondVersion;
    CancellationTokenSource? _signInCancel;
    TaskCompletionSource<bool>? _ask;
    bool _loggedIn; // false while Options is opened from the login screen cog
    bool _joining;
    List<string?> _byondChoices = []; // Options drop-down: null = latest public release
    bool _versionsLoaded;

    public MainWindow()
    {
        InitializeComponent();
        LoginVersion.Text = $"Launcher {Updater.CurrentText}";
        VersionText.Text = Updater.CurrentText;
        UpdatesOnStart.IsChecked = UpdatesOnStartLogin.IsChecked = _settings.CheckUpdatesOnStart;
        ByondUpdatesOnStart.IsChecked = _settings.CheckByondUpdatesOnStart;
        _serverTimer.Tick += async (_, _) => await RefreshWorlds();
        _web = new ByondWeb(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _signInCancel?.Cancel();
        _web.Close();
        base.OnClosed(e);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_settings.CheckUpdatesOnStart)
        {
            StartupText.Text = "Checking for launcher updates…";
            await CheckForUpdate();
        }

        // The launcher keeps its own copy of the official BYOND client, so no BYOND install or admin rights are needed.
        StartupText.Text = "Preparing BYOND…";
        try { _byondVersion = await ManagedByond.Prepare(_settings.ByondVersion, _settings.CheckByondUpdatesOnStart, new Progress<string>(t => StartupText.Text = t)); }
        catch (Exception ex) { await Fatal("BYOND Unavailable", $"The BYOND client could not be downloaded from byond.com: {ex.Message}"); return; }
        ShowByondInfo();

        StartupText.Text = "Checking your BYOND sign-in…";
        if (!await RefreshSession()) return;
        ShowLogin();
    }

    void ShowByondInfo()
    {
        var how = _settings.ByondVersion != null ? "chosen in Options" : "latest public release";
        SetInfo(ByondVersionText, "BYOND Client Version:", $"{_byondVersion} ({how})");
        ByondDirText.Text = $"Stored in {ManagedByond.Root}";
    }

    void ByondUpdatesOnStart_Changed(object? s, RoutedEventArgs e)
    {
        var on = ByondUpdatesOnStart.IsChecked == true;
        if (on == _settings.CheckByondUpdatesOnStart) return;
        _settings.CheckByondUpdatesOnStart = on;
        _settings.Save();
    }

    // Fills the version drop-down from BYOND's build archive the first time Options is opened.
    async void LoadByondVersions()
    {
        if (_versionsLoaded) return;
        _versionsLoaded = true;
        try
        {
            var latest = await ManagedByond.LatestVersion();
            var all = await ManagedByond.ArchiveVersions();
            _byondChoices = [null, .. all];
            ByondVersionPick.ItemsSource = new[] { $"Latest public release ({latest})" }
                .Concat(all.Select(v => Version.Parse(v) > Version.Parse(latest) ? $"{v} (beta)" : v)).ToList();
            ByondVersionPick.SelectedIndex = _settings.ByondVersion is { } chosen && all.IndexOf(chosen) is var i and >= 0 ? i + 1 : 0;
        }
        catch (Exception ex)
        {
            _versionsLoaded = false; // try again next time Options opens
            ByondVersionPick.PlaceholderText = "Version list unavailable";
            ByondStatus.Text = $"Couldn't load BYOND's version list: {ex.Message}";
        }
    }

    async void InstallByond_Click(object? s, RoutedEventArgs e)
    {
        if (ByondVersionPick.SelectedIndex is var i && (i < 0 || i >= _byondChoices.Count)) return;
        var chosen = _byondChoices[ByondVersionPick.SelectedIndex];
        try
        {
            var progress = new Progress<string>(t => ByondStatus.Text = t);
            _byondVersion = await ManagedByond.Install(chosen ?? await ManagedByond.LatestVersion(), progress);
            _settings.ByondVersion = chosen;
            _settings.Save();
            ByondStatus.Text = chosen is null
                ? $"Using the latest public release, BYOND {_byondVersion}."
                : $"Using BYOND {_byondVersion}. The launcher stays on this version until you pick the latest public release.";
        }
        catch (Exception ex) { ByondStatus.Text = $"Couldn't install that version: {ex.Message}"; }
        ShowByondInfo();
    }

    // Downloads the current build again from byond.com, then restarts the launcher.
    async void ReinstallByond_Click(object? s, RoutedEventArgs e)
    {
        if (_byondVersion is not { } v) return;
        if (!await Ask("Reinstall BYOND", $"Delete BYOND {v} and download it again from byond.com?",
                "Close any DMG game windows first. The launcher restarts afterwards.", "REINSTALL", "CANCEL"))
            return;
        try
        {
            ManagedByond.Remove(v);
            await ManagedByond.Install(v, new Progress<string>(t => ByondStatus.Text = t));
        }
        catch (Exception ex) { ByondStatus.Text = $"Couldn't reinstall BYOND (is a game window still open?): {ex.Message}"; return; }
        Restart();
    }

    void SetInfo(TextBlock target, string label, string? value)
    {
        target.Inlines = [new Run(label) { FontFamily = Res<FontFamily>("TextBoldFont") }, new Run($" {value}")];
    }

    // ---------- window chrome ----------

    void Root_PointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    void Minimise_Click(object? s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object? s, RoutedEventArgs e) => Close();

    void Show(Control view)
    {
        StartupView.IsVisible = view == StartupView;
        LoginView.IsVisible = view == LoginView;
        MainView.IsVisible = view == MainView;
    }

    // ---------- dialog ----------

    Task<bool> Ask(string title, string text, string? detail, string yes, string? no = null)
    {
        AskTitle.Text = title;
        AskText.Text = text;
        AskDetail.Text = detail;
        AskDetail.IsVisible = detail != null;
        AskYesText.Text = yes;
        Avalonia.Automation.AutomationProperties.SetName(AskYes, yes);
        AskNo.Content = no;
        AskNo.IsVisible = no != null;
        Overlay.IsVisible = true;
        _ask = new();
        return _ask.Task;
    }

    void Answer(bool yes) { Overlay.IsVisible = false; _ask?.TrySetResult(yes); }
    void AskYes_Click(object? s, RoutedEventArgs e) => Answer(true);
    void AskNo_Click(object? s, RoutedEventArgs e) => Answer(false);

    async Task Fatal(string title, string message)
    {
        await Ask(title, message, null, "QUIT");
        Close();
    }

    void Restart()
    {
        Process.Start(Environment.ProcessPath!)?.Dispose();
        Close();
    }

    // ---------- log in ----------

    void ShowLogin()
    {
        _loggedIn = false;
        _serverTimer.Stop();
        Show(LoginView);
        UpdateLogin();
    }

    // Reads the byond.com session (who is signed in + the id BYOND puts in DMG's Play link). False = fatal.
    async Task<bool> RefreshSession()
    {
        try { _session = await _web.CheckSession(); return true; }
        catch (WebView2RuntimeNotFoundException)
        {
            await Fatal("WebView2 Required", "Microsoft Edge WebView2 Runtime is needed for BYOND sign-in (BYOND itself needs it too). Install it from Microsoft, then start the launcher again.");
            return false;
        }
        catch (Exception ex) { Trace.WriteLine($"session check: {ex.Message}"); _session = new(null, null, null); return true; }
    }

    void UpdateLogin()
    {
        var key = _session.SignedIn ? _session.Key : null;
        KeyBox.Text = key ?? "Not signed in";
        KeyCheck.IsVisible = key != null;
        KeySource.Text = key is null ? "Sign in on BYOND's website to play." : "Signed in on byond.com.";
        PagerLink.Content = key is null ? "Sign in" : "Sign out";
    }

    // Shows BYOND's own login page in the sign-in panel; true once signed in.
    async Task<bool> SignIn()
    {
        WebLogin.IsVisible = true;
        await Task.Delay(50); // let the panel lay out before placing the browser view over it
        var topLeft = WebHost.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var scale = RenderScaling;
        var bounds = new System.Drawing.Rectangle((int)(topLeft.X * scale), (int)(topLeft.Y * scale), (int)(WebHost.Bounds.Width * scale), (int)(WebHost.Bounds.Height * scale));
        _signInCancel = new CancellationTokenSource();
        bool ok;
        try { ok = await _web.SignIn(bounds, _signInCancel.Token); }
        catch (WebView2RuntimeNotFoundException) { ok = false; await Ask("WebView2 Required", "Microsoft Edge WebView2 Runtime is needed for BYOND sign-in.", null, "OK"); }
        finally { WebLogin.IsVisible = false; }
        if (ok) await RefreshSession();
        UpdateLogin();
        return _session.SignedIn;
    }

    void CancelSignIn_Click(object? s, RoutedEventArgs e) => _signInCancel?.Cancel();

    async void SignIn_Click(object? s, RoutedEventArgs e)
    {
        if (!_session.SignedIn) { await SignIn(); return; }
        await _web.SignOut();
        await RefreshSession();
        UpdateLogin();
    }

    void CreateAccount_Click(object? s, RoutedEventArgs e) => Byond.OpenUrl(Byond.JoinPage);
    void Gear_Click(object? s, RoutedEventArgs e) => ShowMain(NavOptions);
    void SwitchAccount_Click(object? s, RoutedEventArgs e) => ShowLogin();

    async void Continue_Click(object? s, RoutedEventArgs e)
    {
        if (!_session.SignedIn && !await SignIn()) return;
        _loggedIn = true;
        ShowMain(NavServers);
    }

    async void ShowMain(Button tab)
    {
        // Before logging in, only Options is reachable: no server list, friends or join.
        NavServers.IsVisible = NavFriends.IsVisible = RailLabel.IsVisible = RailKey.IsVisible = _loggedIn;
        RailKey.Text = _session.Key ?? "";
        RailLink.Content = _loggedIn ? "Switch account" : "Back to Log In";
        Show(MainView);
        SelectTab(tab);
        if (!_loggedIn) return;
        _serverTimer.Start();
        await RefreshWorlds();
    }

    void Nav_Click(object? s, RoutedEventArgs e) => SelectTab((Button)s!);

    void SelectTab(Button tab)
    {
        foreach (var b in new[] { NavServers, NavFriends, NavOptions }) b.Classes.Set("active", b == tab);
        ServersPanel.IsVisible = tab == NavServers;
        FriendsPanel.IsVisible = tab == NavFriends;
        OptionsPanel.IsVisible = tab == NavOptions;
        if (tab == NavOptions) LoadByondVersions();
    }

    // ---------- server list ----------

    async void Refresh_Click(object? s, RoutedEventArgs e) => await RefreshWorlds();

    async Task RefreshWorlds()
    {
        ServerStatus.Text = "Refreshing…";
        try
        {
            _worlds = await Byond.FetchWorlds();
            ServerStatus.Text = $"{_worlds.Count} server{(_worlds.Count == 1 ? "" : "s")} online · updated {DateTime.Now:t}";
        }
        catch (Exception ex) { ServerStatus.Text = $"Couldn't reach the BYOND hub: {ex.Message}"; }

        ServerRows.Children.Clear();
        foreach (var w in _worlds)
        {
            var name = new StackPanel { Spacing = 2 };
            name.Children.Add(new TextBlock { Text = w.Status != "" ? w.Status : w.Address, FontSize = 18, Classes = { "bold" }, TextTrimming = TextTrimming.CharacterEllipsis });
            name.Children.Add(new TextBlock { Text = w.Address, FontSize = 13, Foreground = Res<IBrush>("Muted2") });
            var players = new TextBlock { Text = w.Players.Count.ToString(), FontFamily = Res<FontFamily>("NumberFont"), FontSize = 24, VerticalAlignment = VerticalAlignment.Center };
            ServerRows.Children.Add(Row("*,140,170", name, players, Cta("JOIN", 44, true, () => Join(w))));
        }
        if (_worlds.Count == 0)
            ServerRows.Children.Add(new TextBlock { Text = "No DMG servers are online right now.", Foreground = Res<IBrush>("Muted") });
        RenderFriends();
    }

    // Joins through the launcher's BYOND copy with a fresh web session id. BYOND's own pager runs its normal
    // "Your Game Is Starting" screen (and the non-member wait) before the game opens; nothing skips it.
    async void Join(World w)
    {
        if (_joining) return;
        _joining = true;
        JoinWorldName.Text = w.Name;
        void Overlay(string? status) { JoinOverlay.IsVisible = status != null; if (status != null) JoinStatus.Text = status; }
        try
        {
            Overlay("Checking your BYOND sign-in…");
            if (!await RefreshSession()) return;
            if (!_session.SignedIn)
            {
                Overlay(null);
                if (!await SignIn()) { ServerStatus.Text = "Sign in to BYOND to join."; return; }
            }
            while (ManagedByond.OtherByondRunning())
            {
                Overlay(null);
                if (!await Ask("Close BYOND First", "Another copy of BYOND is open (the pager or a game window).",
                        "BYOND only runs one pager at a time, so that copy would take over the join. Quit it from the system tray, then press Retry.", "RETRY", "CANCEL"))
                { ServerStatus.Text = "Join cancelled."; return; }
            }
            Overlay("BYOND is starting your game…");
            var error = await ManagedByond.Connect(_byondVersion!, w.Url, _session.WebId!, new Progress<string>(t => JoinStatus.Text = t));
            Overlay(null);
            ServerStatus.Text = error ?? "Game opened.";
            if (error != null) await Ask("Couldn't Join", error, null, "OK");
        }
        catch (Exception ex) { Overlay(null); await Ask("Couldn't Join", $"BYOND couldn't start the game: {ex.Message}", null, "OK"); }
        finally { Overlay(null); _joining = false; }
    }

    // ---------- friends ----------

    void RenderFriends()
    {
        FriendRows.Children.Clear();
        foreach (var friend in _settings.Friends)
        {
            var w = _worlds.FirstOrDefault(w => w.Players.Any(p => Byond.Ckey(p) == Byond.Ckey(friend)));
            var online = w != null;
            var colour = online ? new SolidColorBrush(Color.Parse("#8FDC9A")) : Res<IBrush>("Muted2");
            var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(online
                ? new Ellipse { Width = 10, Height = 10, Fill = colour }
                : new Ellipse { Width = 9, Height = 9, Stroke = colour, StrokeThickness = 1.5 });
            var label = new TextBlock { Text = online ? $"On DMG · {w!.Status}" : "Not on DMG", FontSize = 15, Foreground = colour };
            if (online) label.Classes.Add("bold");
            status.Children.Add(label);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            if (online) buttons.Children.Add(Cta("JOIN", 44, false, () => Join(w!), 120));
            var remove = new Button { Classes = { "icon" }, Background = Brushes.Transparent, Content = CrossIcon() };
            ToolTip.SetTip(remove, $"Remove {friend}");
            Avalonia.Automation.AutomationProperties.SetName(remove, $"Remove {friend}");
            remove.Click += (_, _) => { _settings.Friends.Remove(friend); _settings.Save(); RenderFriends(); };
            buttons.Children.Add(remove);

            var key = new TextBlock { Text = friend, FontSize = 18, Classes = { "bold" }, VerticalAlignment = VerticalAlignment.Center };
            FriendRows.Children.Add(Row("*,250,190", key, status, buttons));
        }
        if (_settings.Friends.Count == 0)
            FriendRows.Children.Add(new TextBlock { Text = "No friends added yet.", Foreground = Res<IBrush>("Muted") });
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

        var v = r.Version.ToString(3);
        UpdateStatus.Text = $"Version {v} is available.";
        if (!await Ask("Update Available", $"DMG Launcher {v} is available. You have {Updater.CurrentText}.", "The launcher restarts after updating.", "UPDATE", "SKIP"))
            return;
        UpdateStatus.Text = StartupText.Text = "Downloading update…";
        try { await Updater.Apply(r); }
        catch (Exception ex) { await Ask("Update Failed", $"The update failed: {ex.Message}", null, "OK"); }
    }

    void UpdatesOnStart_Changed(object? s, RoutedEventArgs e)
    {
        var on = ((CheckBox)s!).IsChecked == true;
        if (on == _settings.CheckUpdatesOnStart) return;
        _settings.CheckUpdatesOnStart = on;
        UpdatesOnStart.IsChecked = UpdatesOnStartLogin.IsChecked = on;
        _settings.Save();
    }

    void ResetSettings_Click(object? s, RoutedEventArgs e)
    {
        new Settings { Friends = _settings.Friends }.Save();
        Restart();
    }

    async void ClearData_Click(object? s, RoutedEventArgs e)
    {
        if (!await Ask("Clear Stored Data", "Delete all stored launcher data, including your friends list?", "The launcher restarts afterwards.", "CLEAR DATA", "CANCEL"))
            return;
        try { Settings.ClearAll(); }
        catch (Exception ex) { OptionsStatus.Text = $"Couldn't clear data: {ex.Message}"; return; }
        Restart();
    }

    // ---------- shared pieces ----------

    T Res<T>(string key) => (T)Application.Current!.FindResource(key)!;

    static Border Row(string columns, Control a, Control b, Control c)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        Grid.SetColumn(b, 1);
        Grid.SetColumn(c, 2);
        grid.Children.AddRange([a, b, c]);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#08FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#332E4C")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 10),
            Child = grid,
        };
    }

    static Button Cta(string text, double height, bool arrow, Action onClick, double width = double.NaN)
    {
        var label = new TextBlock { Text = text, FontSize = 17, LetterSpacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(arrow ? $"*,{height}" : "*") };
        grid.Children.Add(label);
        if (arrow)
        {
            var seg = new Border { Classes = { "ctaseg" }, Child = ArrowIcon(22) };
            Grid.SetColumn(seg, 1);
            grid.Children.Add(seg);
        }
        var b = new Button { Classes = { "cta" }, Height = height, Width = width, HorizontalAlignment = HorizontalAlignment.Stretch, Content = grid };
        Avalonia.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    static Control ArrowIcon(double size) => new Viewbox
    {
        Width = size,
        Height = size,
        Child = new Grid
        {
            Width = 28,
            Height = 28,
            Children =
            {
                new Ellipse { Width = 22, Height = 22, Stroke = new SolidColorBrush(Color.Parse("#1C1206")), StrokeThickness = 2 },
                new Path { Data = Geometry.Parse("M10,14 L18,14 M14.5,10 L18.5,14 L14.5,18"), Stroke = new SolidColorBrush(Color.Parse("#1C1206")), StrokeThickness = 2 },
            },
        },
    };

    static Control CrossIcon() => new Path
    {
        Data = Geometry.Parse("M3,3 L13,13 M13,3 L3,13"),
        Width = 16,
        Height = 16,
        StrokeThickness = 1.8,
        [!Shape.StrokeProperty] = new Avalonia.Data.Binding("Foreground") { RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor) { AncestorType = typeof(Button) } },
    };
}
