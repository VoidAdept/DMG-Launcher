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
using Path = Avalonia.Controls.Shapes.Path;

namespace DMGLauncher;

public partial class MainWindow : Window
{
    Settings _settings = Settings.Load();
    List<World> _worlds = [];
    readonly DispatcherTimer _loginTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _serverTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _byondWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    TaskCompletionSource<bool>? _ask;

    public MainWindow()
    {
        InitializeComponent();
        LoginVersion.Text = $"Launcher {Updater.CurrentText}";
        VersionText.Text = Updater.CurrentText;
        UpdatesOnStart.IsChecked = UpdatesOnStartLogin.IsChecked = _settings.CheckUpdatesOnStart;
        _loginTimer.Tick += (_, _) => UpdateLogin();
        _serverTimer.Tick += async (_, _) => await RefreshWorlds();
        // The launcher only makes sense next to a running pager, so it closes with it.
        _byondWatch.Tick += (_, _) => { if (!Byond.IsRunning) Close(); };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_settings.CheckUpdatesOnStart)
        {
            StartupText.Text = "Checking for launcher updates…";
            await CheckForUpdate();
        }

        StartupText.Text = "Checking for BYOND…";
        if (Byond.PagerExe is null)
        {
            if (await Ask("BYOND Required", "BYOND is needed to play Duel Monsters Genesis, but it isn't detected on this computer.",
                    "Click Install to visit the BYOND website to download and install the BYOND client software, then restart this launcher to continue.",
                    "INSTALL", "QUIT"))
                Byond.OpenUrl(Byond.DownloadPage);
            Close();
            return;
        }

        StartupText.Text = "Starting BYOND…";
        if (!await Byond.EnsureRunning())
        {
            await Fatal("BYOND Not Running", $"BYOND could not be started after {Byond.MaxLaunchTries} tries. The launcher will now close.");
            return;
        }

        _byondWatch.Start();
        SetInfo(ByondDirText, "BYOND Install Directory:", Byond.InstallDir);
        SetInfo(ByondVersionText, "BYOND Client Version:", Byond.ClientVersion ?? "unknown");
        ShowLogin();
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
        _serverTimer.Stop();
        Show(LoginView);
        UpdateLogin();
        _loginTimer.Start();
    }

    void UpdateLogin()
    {
        var key = Byond.SignedInKey;
        KeyBox.Text = key ?? "Not signed in";
        KeyCheck.IsVisible = key != null;
        KeySource.Text = key is null ? "Sign in through the BYOND pager." : "Detected from the BYOND pager.";
        PagerLink.Content = key is null ? "Sign in with BYOND" : "Switch account in BYOND";
        ContinueButton.IsEnabled = key != null;
    }

    void SignIn_Click(object? s, RoutedEventArgs e) => Byond.ShowPager();
    void CreateAccount_Click(object? s, RoutedEventArgs e) => Byond.OpenUrl(Byond.JoinPage);
    void Gear_Click(object? s, RoutedEventArgs e) => ShowMain(NavOptions);
    void SwitchAccount_Click(object? s, RoutedEventArgs e) => ShowLogin();

    void Continue_Click(object? s, RoutedEventArgs e)
    {
        if (Byond.SignedInKey is null) { UpdateLogin(); return; }
        ShowMain(NavServers);
    }

    async void ShowMain(Button tab)
    {
        _loginTimer.Stop();
        RailKey.Text = Byond.SignedInKey ?? "Not signed in";
        Show(MainView);
        SelectTab(tab);
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
            ServerRows.Children.Add(Row("*,140,170", name, players, Cta("JOIN", 44, true, () => Byond.Join(w))));
        }
        if (_worlds.Count == 0)
            ServerRows.Children.Add(new TextBlock { Text = "No DMG servers are online right now.", Foreground = Res<IBrush>("Muted") });
        RenderFriends();
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
            if (online) buttons.Children.Add(Cta("JOIN", 44, false, () => Byond.Join(w!), 120));
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
