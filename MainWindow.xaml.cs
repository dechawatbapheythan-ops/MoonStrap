using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MoonStrap;

public partial class MainWindow : Window
{
    Settings _s = Settings.Load();
    bool _loading = true;
    readonly List<(Ellipse dot, double fx, double fy)> _stars = new();
    readonly List<(Ellipse dot, DoubleAnimation anim)> _twinkles = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RoundCorners();
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            if (WindowState == WindowState.Minimized) StopAmbience(); else StartAmbience();
        };
        SizeChanged += (_, _) => PlaceStars();
        Loaded += (_, _) =>
        {
            BuildStars();
            StartAmbience();
            StartMeteors();
            MoveNavIndicator(NavItems.Children[0] as RadioButton, animate: false);
            PlayIntro();
            _ = LoadAccountAsync();
            WatchAccountFile();
        };

        LoadIntoUi();
        _loading = false;
    }

    // ---------------- setup ----------------

    void LoadIntoUi()
    {
        if (_s.MigrateCustomFlags()) _s.Save(); // before the Tweaks controls read _s
        Greeting.Text = GreetingFor(DateTime.Now);
        StartGreetingClock();

        FpsBox.Text = _s.FpsCap.ToString();
        SyncFpsChips();
        foreach (RadioButton rb in RenderChips.Children)
            rb.IsChecked = (string)rb.Content == _s.RenderApi;
        FullscreenBox.IsChecked = _s.ExclusiveFullscreen;
        MultiInstanceBox.IsChecked = _s.MultiInstance;
        GpuBox.IsChecked = _s.UseDedicatedGpu;
        RefreshGpuNote();
        foreach (RadioButton rb in MsaaChips.Children)
            rb.IsChecked = (string)rb.Tag == _s.Msaa.ToString();
        RefreshVersionMode();
        FlagsBox.Text = Settings.LoadFlagsText();
        BuildFlagRows();
        ValidateFlags();
        RefreshStatus();
        RefreshMods();
    }

    void RefreshStatus()
    {
        RefreshProtocol();
        StudioVersionText.Text = _s.InstalledStudioVersion is { } sv
            ? $"Studio: {sv}"
            : "Studio: downloads on first launch (~560 MB)";
        bool installed = _s.InstalledVersion != null;
        if (_s.PinnedVersion != null)
        {
            VersionText.Text = $"Player: pinned to {_s.PinnedLabel ?? _s.PinnedVersion} (auto-update off)";
            HeroSub.Text = _s.InstalledVersion == _s.PinnedVersion
                ? "Running an older build. It may not be able to join live games."
                : "Your pinned older build will be downloaded on the next launch.";
            return;
        }
        VersionText.Text = installed
            ? $"Player: {_s.InstalledVersion}"
            : "Player: downloads on first launch (~150 MB)";
        HeroSub.Text = installed
            ? "Your client is installed and tuned. One click and you're in orbit."
            : "First launch downloads Roblox straight from Roblox's servers (about 150 MB).";
    }

    void RefreshProtocol()
    {
        bool reg = Protocol.IsRegistered && Protocol.IsStudioRegistered;
        ProtocolText.Text = reg
            ? "Play and Edit buttons on roblox.com open through MoonStrap."
            : Protocol.IsRegistered
                ? "Play buttons use MoonStrap, but Edit (Studio) buttons don't yet. Click below to take over both."
                : "Play and Edit buttons on roblox.com use " + OtherLauncherName() + ". Take over to apply your tweaks.";
        RegisterBtn.Content = reg ? "Restore previous launcher" : "Make MoonStrap default";
        RegisterBtn.Style = reg ? (Style)FindResource(typeof(Button)) : (Style)FindResource("Primary");
        ProtoPill.Background = new SolidColorBrush(reg ? Color.FromArgb(0x26, 0x7E, 0xE2, 0xB8) : Color.FromArgb(0x26, 0xFF, 0x8F, 0xA3));
        ProtoPillText.Foreground = (Brush)FindResource(reg ? "Good" : "Bad");
        ProtoPillText.Text = reg ? "Active" : "Inactive";
    }

    static string OtherLauncherName()
    {
        var cmd = Protocol.CurrentCommand() ?? "";
        if (cmd.Contains("Bloxstrap", StringComparison.OrdinalIgnoreCase)) return "Bloxstrap";
        if (cmd.Contains("Fishstrap", StringComparison.OrdinalIgnoreCase)) return "Fishstrap";
        return cmd.Length == 0 ? "nothing yet" : "the official launcher";
    }

    // ---------------- window chrome ----------------

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int round = 2;            // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int border = 0x00402A2A;  // COLORREF (BGR) soft indigo border
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    // ---------------- ambience ----------------

    void BuildStars()
    {
        var rng = new Random(7);
        for (int i = 0; i < 140; i++)
        {
            double size = rng.NextDouble() < 0.85 ? 1 + rng.NextDouble() : 2 + rng.NextDouble();
            var dot = new Ellipse
            {
                Width = size, Height = size,
                Fill = new SolidColorBrush(rng.NextDouble() < 0.2 ? Color.FromRgb(0xFF, 0xEE, 0xC8) : Color.FromRgb(0xE4, 0xE1, 0xFF)),
                Opacity = 0.15 + rng.NextDouble() * 0.6,
            };
            if (rng.NextDouble() < 0.18)
            {
                // twinkling stars live on their own layer so they don't invalidate the cached starfield
                var anim = new DoubleAnimation(dot.Opacity, 0.05, TimeSpan.FromSeconds(1.5 + rng.NextDouble() * 3))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(rng.NextDouble() * 3),
                };
                Timeline.SetDesiredFrameRate(anim, 24);
                _twinkles.Add((dot, anim));
                Twinkles.Children.Add(dot);
            }
            else Stars.Children.Add(dot);
            // spread a little past the edges so the slow drift never reveals an empty strip
            _stars.Add((dot, rng.NextDouble() * 1.1 - 0.05, rng.NextDouble() * 1.1 - 0.05));
        }
        PlaceStars();
    }

    void PlaceStars()
    {
        foreach (var (dot, fx, fy) in _stars)
        {
            Canvas.SetLeft(dot, fx * ActualWidth);
            Canvas.SetTop(dot, fy * ActualHeight);
        }
    }

    void StartMoonFloat() => MoonFloat.BeginAnimation(TranslateTransform.YProperty, Loop(-6, 6, 4, fps: 40));

    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction EaseInOut = new SineEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>Endless back-and-forth animation. Slow ambient motion runs at a reduced frame rate:
    /// it looks identical but costs a fraction of the GPU/CPU time of 60 fps.</summary>
    static DoubleAnimation Loop(double from, double to, double seconds, double delay = 0, int fps = 30)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseInOut,
            BeginTime = TimeSpan.FromSeconds(delay),
        };
        Timeline.SetDesiredFrameRate(a, fps);
        return a;
    }

    bool _ambienceRunning;

    /// <summary>Slow, continuous background motion: breathing glows and a drifting starfield.</summary>
    void StartAmbience()
    {
        if (_ambienceRunning) return;
        _ambienceRunning = true;
        HeroGlow.BeginAnimation(OpacityProperty, Loop(0.55, 1, 3.2));
        AmbientGlow.BeginAnimation(OpacityProperty, Loop(0.7, 1, 6, fps: 15));
        // only the glow's opacity breathes; animating blur radius would re-render the effect every frame
        LaunchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, Loop(0.35, 0.85, 1.8));
        StarDrift.BeginAnimation(TranslateTransform.XProperty, Loop(-18, 18, 40, fps: 20));
        StarDrift.BeginAnimation(TranslateTransform.YProperty, Loop(-8, 8, 28, fps: 20));
        Twinkles.RenderTransform = StarDrift; // twinkling layer drifts with the rest
        foreach (var (dot, anim) in _twinkles) dot.BeginAnimation(OpacityProperty, anim);
        StartMoonFloat();
    }

    /// <summary>Stops all endless animations, e.g. while minimised, so MoonStrap idles at ~0% CPU/GPU.</summary>
    void StopAmbience()
    {
        if (!_ambienceRunning) return;
        _ambienceRunning = false;
        HeroGlow.BeginAnimation(OpacityProperty, null);
        AmbientGlow.BeginAnimation(OpacityProperty, null);
        LaunchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
        StarDrift.BeginAnimation(TranslateTransform.XProperty, null);
        StarDrift.BeginAnimation(TranslateTransform.YProperty, null);
        MoonFloat.BeginAnimation(TranslateTransform.YProperty, null);
        foreach (var (dot, _) in _twinkles) dot.BeginAnimation(OpacityProperty, null);
    }

    /// <summary>Window entrance: fade in and rise, then stagger the first page's cards.</summary>
    void PlayIntro()
    {
        Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = EaseOut });
        ShellShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = EaseOut });
        StaggerIn(PageHome, startDelayMs: 120);
    }

    /// <summary>Each top-level block of a page slides up and fades in, one after another.</summary>
    static void StaggerIn(FrameworkElement page, int startDelayMs = 0)
    {
        var panel = (page as ScrollViewer)?.Content as Panel ?? page as Panel;
        if (panel == null) return;
        int i = 0;
        foreach (UIElement child in panel.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (child.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
                child.RenderTransform = tt = new TranslateTransform();

            // each item waits its turn (held invisible + lowered), then eases into place.
            // FillBehavior.Stop hands control back to the base values (opacity 1, offset 0) at the end.
            var wait = TimeSpan.FromMilliseconds(startDelayMs + i * 55);
            child.BeginAnimation(OpacityProperty, Keyframes(0, 1, wait, 340));
            tt.BeginAnimation(TranslateTransform.YProperty, Keyframes(18, 0, wait, 420));
            i++;
        }

        static DoubleAnimationUsingKeyFrames Keyframes(double from, double to, TimeSpan wait, int ms)
        {
            var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(wait)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(wait + TimeSpan.FromMilliseconds(ms)), EaseOut));
            return a;
        }
    }

    /// <summary>Slides the shared pill/accent indicator to the selected nav item.</summary>
    void MoveNavIndicator(RadioButton? item, bool animate = true)
    {
        if (item == null) return;
        double y = item.TranslatePoint(new Point(0, 0), NavHost).Y;
        NavIndicator.Height = item.ActualHeight > 0 ? item.ActualHeight : 40;
        if (!animate) { NavIndicatorY.Y = y; return; }
        NavIndicatorY.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(360)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
    }

    // ---------------- shooting stars ----------------

    readonly Random _meteorRng = new();
    System.Windows.Threading.DispatcherTimer? _meteorTimer;

    void StartMeteors()
    {
        _meteorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _meteorTimer.Tick += (_, _) =>
        {
            _meteorTimer!.Interval = TimeSpan.FromSeconds(3 + _meteorRng.NextDouble() * 6);
            if (WindowState != WindowState.Minimized && IsVisible) SpawnMeteor();
        };
        _meteorTimer.Start();
        Closed += (_, _) => _meteorTimer.Stop();
    }

    void SpawnMeteor()
    {
        double w = Meteors.ActualWidth, h = Meteors.ActualHeight;
        if (w < 10) return;
        double len = 90 + _meteorRng.NextDouble() * 80;
        var streak = new Border
        {
            Width = len, Height = 2, CornerRadius = new CornerRadius(1),
            Background = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(230, 255, 246, 220), 0),
            Opacity = 0,
        };
        streak.Effect = new System.Windows.Media.Effects.DropShadowEffect
        { Color = Color.FromRgb(0xC9, 0xC0, 0xFF), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };

        double angle = 22 + _meteorRng.NextDouble() * 14;       // heading down-left -> right
        double startX = w * (0.2 + _meteorRng.NextDouble() * 0.7);
        double startY = h * _meteorRng.NextDouble() * 0.35;
        double travel = 260 + _meteorRng.NextDouble() * 200;
        double rad = angle * Math.PI / 180;

        var move = new TranslateTransform(startX, startY);
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform(180 - angle, len, 1)); // tail trails behind the head
        group.Children.Add(move);
        streak.RenderTransform = group;
        Meteors.Children.Add(streak);

        var dur = TimeSpan.FromMilliseconds(900 + _meteorRng.NextDouble() * 500);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(startX, startX - Math.Cos(rad) * travel, dur) { EasingFunction = EaseOut });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, startY + Math.Sin(rad) * travel, dur) { EasingFunction = EaseOut });

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.6)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Duration = dur;
        fade.Completed += (_, _) => Meteors.Children.Remove(streak);
        streak.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- live greeting ----------------

    static string GreetingFor(DateTime now) => now.Hour switch
    {
        < 5 or >= 21 => "GOOD NIGHT",
        < 12 => "GOOD MORNING",
        < 17 => "GOOD AFTERNOON",
        _ => "GOOD EVENING",
    };

    /// <summary>Re-checks the clock every 30 s (cheap: one string compare) and cross-fades when the greeting changes.</summary>
    void StartGreetingClock()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            var next = GreetingFor(DateTime.Now);
            if (next == Greeting.Text) return;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = EaseOut };
            fadeOut.Completed += (_, _) =>
            {
                Greeting.Text = next;
                Greeting.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = EaseOut });
            };
            Greeting.BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    // ---------------- Roblox account (sidebar) ----------------

    RobloxAccount? _account;
    string? _shownAvatar;

    /// <summary>Shows the signed-in account: cached data instantly, then fresh names + headshot from Roblox.</summary>
    async Task LoadAccountAsync()
    {
        var acc = AccountService.ReadLocal();
        if (acc == null)
        {
            _account = null;
            AccountDisplay.Text = "Not signed in";
            AccountUser.Text = "Launch Roblox to sign in";
            AccountCard.ToolTip = null;
            AvatarCircle.Opacity = 0;
            _shownAvatar = null;
            return;
        }

        bool switched = _account?.UserId != acc.UserId;
        ShowAccount(acc);
        AccountLog.Sync(acc, AccountService.ReadKnownAccounts()); // records a switch if the account changed
        if (switched && PageAccounts.Visibility == Visibility.Visible) _ = RefreshAccountsPageAsync();
        if (switched && AccountService.CachedHeadshot(acc.UserId) is { } cached) ShowAvatar(cached, animate: false);

        var fresh = await AccountService.RefreshAsync(acc);
        if (_account?.UserId == fresh.UserId) ShowAccount(fresh);
        if (await AccountService.DownloadHeadshotAsync(acc.UserId) is { } path && _account?.UserId == acc.UserId)
            ShowAvatar(path, animate: _shownAvatar == null);
    }

    void ShowAccount(RobloxAccount acc)
    {
        _account = acc;
        AccountDisplay.Text = acc.DisplayName;
        AccountUser.Text = "@" + acc.Username;
        AccountCard.ToolTip = "Open your Roblox profile";
    }

    void ShowAvatar(string path, bool animate)
    {
        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit();
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;            // don't lock the file
        img.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache; // pick up new headshots
        img.UriSource = new Uri(path);
        img.DecodePixelWidth = 96;
        img.EndInit();
        img.Freeze();
        AvatarCircle.Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        _shownAvatar = path;

        if (!animate) { AvatarCircle.Opacity = 1; return; }
        // pop in: fade + slight overshoot scale
        AvatarCircle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = EaseOut });
        var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        AvatarScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        AvatarScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    // ---------------- Accounts page ----------------

    readonly Dictionary<long, Brush> _avatarBrushes = new();

    Brush? AvatarBrushFor(long id, string? path = null)
    {
        if (_avatarBrushes.TryGetValue(id, out var b) && path == null) return b;
        path ??= AccountService.CachedHeadshot(id);
        if (path == null) return null;
        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 80;
            img.EndInit();
            img.Freeze();
            var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return _avatarBrushes[id] = brush;
        }
        catch { return null; }
    }

    async Task RefreshAccountsPageAsync()
    {
        var active = AccountService.ReadLocal();
        var known = AccountService.ReadKnownAccounts();
        var log = AccountLog.Sync(active, known);

        // accounts on this PC: Roblox's list, plus anything only seen in our log; active one first
        var lastSeen = log.GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Max(e => e.Time));
        var people = known.Select(k => (k.UserId, k.Username, k.DisplayName, k.LastSignIn, k.LoginMethod))
            .Concat(log.Where(e => known.All(k => k.UserId != e.UserId))
                       .GroupBy(e => e.UserId)
                       .Select(g => g.OrderByDescending(e => e.Time).First())
                       .Select(e => (e.UserId, e.Username, e.DisplayName, LastSignIn: (DateTime?)null, LoginMethod: (string?)null)))
            .ToList();

        var rows = people
            .Select(p =>
            {
                bool isActive = active?.UserId == p.UserId;
                DateTime? seen = new[] { p.LastSignIn, lastSeen.TryGetValue(p.UserId, out var l) ? l : (DateTime?)null }.Max();
                string subtitle = isActive ? "Signed in now"
                    : seen is { } t ? "Last signed in " + AvatarRow.Ago(t) : "Remembered by Roblox";
                return new AccountRow
                {
                    UserId = p.UserId, DisplayName = p.DisplayName, Username = "@" + p.Username,
                    Subtitle = subtitle, IsActive = isActive, AvatarBrush = AvatarBrushFor(p.UserId),
                };
            })
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => lastSeen.TryGetValue(r.UserId, out var t) ? t : DateTime.MinValue)
            .ToList();

        KnownList.ItemsSource = rows;
        KnownHeader.Text = rows.Count == 1 ? "On this PC · 1 account" : $"On this PC · {rows.Count} accounts";
        KnownEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var switchDot = (Brush)FindResource("Accent");
        var signinDot = (Brush)FindResource("MoonGold");
        var activity = log.Take(60).Select(e => new ActivityRow
        {
            UserId = e.UserId, DisplayName = e.DisplayName, Username = "@" + e.Username,
            Verb = e.Kind == "switch" ? "Switched to" : "Signed in as",
            TimeText = AvatarRow.When(e.Time),
            DotBrush = e.Kind == "switch" ? switchDot : signinDot,
            AvatarBrush = AvatarBrushFor(e.UserId),
        }).ToList();
        ActivityList.ItemsSource = activity;
        ActivityEmpty.Visibility = activity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // fetch any missing / stale headshots in one batch, then fill them in
        var paths = await AccountService.DownloadHeadshotsAsync(rows.Select(r => r.UserId).Concat(activity.Select(a => a.UserId)));
        foreach (var (id, path) in paths)
        {
            var brush = AvatarBrushFor(id, path);
            foreach (var r in rows.Where(r => r.UserId == id)) r.AvatarBrush = brush;
            foreach (var a in activity.Where(a => a.UserId == id)) a.AvatarBrush = brush;
        }
    }

    void OpenProfile_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo((string)((Button)sender).Tag) { UseShellExecute = true });

    void ClearAccountLog_Click(object sender, RoutedEventArgs e)
    {
        AccountLog.Clear();
        _ = RefreshAccountsPageAsync();
        ShowToast("Activity log cleared");
    }

    // ---------------- live account switch detection ----------------

    FileSystemWatcher? _accountWatcher;
    System.Windows.Threading.DispatcherTimer? _accountDebounce;

    /// <summary>Roblox rewrites appStorage.json when you sign in/out; react within a second or so.</summary>
    void WatchAccountFile()
    {
        try
        {
            if (!Directory.Exists(AccountService.StorageFolder)) return;
            _accountDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _accountDebounce.Tick += (_, _) =>
            {
                _accountDebounce.Stop();
                if (AccountService.ReadLocal()?.UserId != _account?.UserId) _ = LoadAccountAsync();
            };
            _accountWatcher = new FileSystemWatcher(AccountService.StorageFolder, AccountService.StorageFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler kick = (_, _) => Dispatcher.BeginInvoke(() => { _accountDebounce.Stop(); _accountDebounce.Start(); });
            _accountWatcher.Changed += kick;
            _accountWatcher.Created += kick;
            _accountWatcher.Renamed += (s, e) => kick(s, e);
            Closed += (_, _) => _accountWatcher.Dispose();
        }
        catch { }
    }

    void Account_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_account != null)
            Process.Start(new ProcessStartInfo(_account.ProfileUrl) { UseShellExecute = true });
        else
            ShowToast("Sign in inside Roblox, then come back", ok: false);
    }

    void ShowToast(string text, bool ok = true)
    {
        ToastText.Text = text;
        ToastIcon.Text = ok ? "" : "";
        ToastIcon.Foreground = (Brush)FindResource(ok ? "Good" : "Bad");
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), EaseOut));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        Toast.BeginAnimation(OpacityProperty, a);

        // slide up from below with a little overshoot, then sink away
        if (Toast.RenderTransform is not TranslateTransform t || t.IsFrozen) Toast.RenderTransform = t = new TranslateTransform();
        var slide = new DoubleAnimationUsingKeyFrames();
        slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(24, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }));
        slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        slide.KeyFrames.Add(new EasingDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        t.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    // ---------------- navigation ----------------

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHome == null) return; // fires during InitializeComponent
        var item = (RadioButton)sender;
        var target = (string)item.CommandParameter;
        MoveNavIndicator(item);
        if (target == "PageAccounts") _ = RefreshAccountsPageAsync();
        foreach (var page in new FrameworkElement[] { PageHome, PageTweaks, PageFlags, PageMods, PageVersions, PageAccounts })
        {
            bool show = page.Name == target;
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) StaggerIn(page);
        }
        if (target == "PageMods") RefreshMods();
        if (target == "PageFlags") RefreshFlagRows();
        if (target == "PageVersions") _ = LoadHistoryAsync();
    }

    // ---------------- versions ----------------

    List<Deployment.HistoryEntry>? _history;

    void RefreshVersionMode()
    {
        bool pinned = _s.PinnedVersion != null;
        ModeIcon.Text = pinned ? "" : "";   // lock / sync
        ModeTitle.Text = pinned ? $"Pinned to {_s.PinnedLabel ?? _s.PinnedVersion}" : "Latest (auto-update)";
        ModeNote.Text = pinned
            ? $"{_s.PinnedVersion} · MoonStrap won't update Roblox until you go back to latest."
            : "MoonStrap installs the newest Roblox before every launch.";
        BackToLatestBtn.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task LoadHistoryAsync()
    {
        if (_history != null) return;
        try
        {
            _history = await Deployment.GetHistoryAsync();
            HistoryStatus.Visibility = Visibility.Collapsed;
            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            HistoryStatus.Text = "Couldn't load version history: " + ex.Message;
        }
    }

    void ApplyHistoryFilter()
    {
        if (_history == null) return;
        var q = HistorySearch.Text.Trim();
        var items = q.Length == 0 ? _history : _history.Where(h =>
            h.Version.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            h.Hash.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            h.DateText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        HistoryList.ItemsSource = items;
        HistoryHeader.Text = $"Past builds · {items.Count}";
    }

    void HistorySearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyHistoryFilter();

    void Pin(string hash, string? label)
    {
        // remember the current latest build so it survives the pinned install
        if (_s.PinnedVersion == null && _s.InstalledVersion != null) _s.LatestPlayerVersion ??= _s.InstalledVersion;
        _s.PinnedVersion = hash;
        _s.LastPinnedVersion = hash;
        _s.PinnedLabel = label;
        _s.Save();
        RefreshVersionMode();
        RefreshStatus();
        ShowToast($"Pinned to {label ?? hash}. It downloads on next launch");
    }

    void PinHistory_Click(object sender, RoutedEventArgs e)
    {
        var entry = (Deployment.HistoryEntry)((Button)sender).Tag;
        Pin(entry.Hash, entry.Version);
    }

    async void PinManual_Click(object sender, RoutedEventArgs e)
    {
        var hash = ManualVersionBox.Text.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^version-[0-9a-f]{16}$"))
        {
            ShowToast("IDs look like version-1a2b3c4d5e6f7a8b", ok: false);
            return;
        }
        PinManualBtn.IsEnabled = false;
        bool exists = await Deployment.VersionExistsAsync(hash);
        PinManualBtn.IsEnabled = true;
        if (!exists)
        {
            ShowToast("Roblox's servers don't have that version", ok: false);
            return;
        }
        Pin(hash, _history?.FirstOrDefault(h => h.Hash == hash)?.Version);
    }

    void BackToLatest_Click(object sender, RoutedEventArgs e)
    {
        _s.PinnedVersion = null;
        _s.PinnedLabel = null;
        _s.Save();
        RefreshVersionMode();
        RefreshStatus();
        bool latestOnDisk = _s.LatestPlayerVersion is { } l &&
            File.Exists(System.IO.Path.Combine(Deployment.VersionDir(l), Deployment.ExeName(Product.Player)));
        ShowToast(latestOnDisk ? "Back on latest. Already downloaded" : "Back on latest. Downloads on next launch");
    }

    // ---------------- GPU ----------------

    void RefreshGpuNote()
    {
        var gpu = GpuPreference.DedicatedGpuName();
        bool hw = (RenderCapability.Tier >> 16) == 2;
        GpuTitle.Text = gpu != null ? $"Use {gpu.Replace(" Laptop GPU", "")}" : "Use dedicated GPU";
        GpuNote.Text = (gpu == null
                ? "No dedicated graphics card found. Windows will use the built-in GPU."
                : "Runs MoonStrap's animated interface and Roblox on your dedicated graphics card instead of the built-in Intel GPU. Uses more battery.")
            + (hw ? "" : "  (Hardware acceleration is currently unavailable.)");
    }

    void Gpu_Click(object sender, RoutedEventArgs e)
    {
        _s.UseDedicatedGpu = GpuBox.IsChecked == true;
        _s.Save();
        GpuPreference.Set(Paths.Exe, _s.UseDedicatedGpu);
        foreach (var (v, p) in new[] { (_s.InstalledVersion, Product.Player), (_s.InstalledStudioVersion, Product.Studio) })
            if (v != null) GpuPreference.Set(System.IO.Path.Combine(Deployment.VersionDir(v), Deployment.ExeName(p)), _s.UseDedicatedGpu);
        ShowToast(_s.UseDedicatedGpu ? "Dedicated GPU on. Restart MoonStrap to switch" : "Windows will choose the GPU");
    }

    void Msaa_Checked(object sender, RoutedEventArgs e)
    {
        _s.Msaa = int.Parse((string)((RadioButton)sender).Tag);
        SaveTweaks();
    }

    // ---------------- tweaks (auto-save) ----------------

    void SaveTweaks()
    {
        if (_loading) return;
        _s.Save();
        ShowToast("Saved");
    }

    void SyncFpsChips()
    {
        foreach (var child in FpsChips.Children)
            if (child is RadioButton rb)
                rb.IsChecked = (string)rb.Tag == _s.FpsCap.ToString();
    }

    void FpsChip_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.FpsCap = int.Parse((string)((RadioButton)sender).Tag);
        _loading = true; FpsBox.Text = _s.FpsCap.ToString(); _loading = false;
        SaveTweaks();
    }

    void FpsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(FpsBox.Text, out var fps) && fps >= 0 && fps <= 9999)
        {
            _s.FpsCap = fps;
            _loading = true; SyncFpsChips(); _loading = false;
            _s.Save();
        }
    }

    void Render_Checked(object sender, RoutedEventArgs e)
    {
        _s.RenderApi = (string)((RadioButton)sender).Content;
        SaveTweaks();
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _s.ExclusiveFullscreen = FullscreenBox.IsChecked == true;
        _s.MultiInstance = MultiInstanceBox.IsChecked == true;
        SaveTweaks();
    }

    // ---------------- fastflags ----------------

    bool ValidateFlags()
    {
        try
        {
            if (JsonNode.Parse(FlagsBox.Text) is not JsonObject obj) throw new FormatException("Must be a JSON object { ... }");
            FlagsDot.Fill = (Brush)FindResource("Good");
            FlagsStatus.Text = obj.Count == 1 ? "Valid JSON · 1 flag" : $"Valid JSON · {obj.Count} flags";
            return true;
        }
        catch (Exception ex)
        {
            FlagsDot.Fill = (Brush)FindResource("Bad");
            FlagsStatus.Text = "Invalid: " + ex.Message.Split('\n')[0];
            return false;
        }
    }

    void FlagsBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateFlags();

    void FormatFlags_Click(object sender, RoutedEventArgs e)
    {
        if (ValidateFlags())
            FlagsBox.Text = JsonNode.Parse(FlagsBox.Text)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // ---------------- fastflag rows ----------------

    readonly Dictionary<string, (CheckBox sw, TextBox? box)> _flagRows = new();
    readonly Dictionary<string, TextBlock> _tweakRowState = new();

    /// <summary>Builds one card per group from FlagCatalog and puts them where FlagRowsAnchor sits.</summary>
    void BuildFlagRows()
    {
        int at = FlagsPanel.Children.IndexOf(FlagRowsAnchor);
        FlagsPanel.Children.Remove(FlagRowsAnchor);
        foreach (var group in FlagCatalog.All.GroupBy(d => d.Group))
        {
            FlagsPanel.Children.Insert(at++, new TextBlock
            {
                Text = group.Key.ToUpperInvariant(), Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 16, 0, 8),
            });
            var list = new StackPanel();
            bool first = true;
            foreach (var def in group)
            {
                if (!first) list.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("CardBorder"), Margin = new Thickness(0, 14, 0, 14) });
                first = false;
                list.Children.Add(BuildFlagRow(def));
            }
            FlagsPanel.Children.Insert(at++, new Border { Style = (Style)FindResource("Card"), Child = list });
        }
        RefreshFlagRows();
    }

    FrameworkElement BuildFlagRow(FlagDef def)
    {
        var row = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);

        if (def.Kind == FlagKind.Tweaks)
        {
            var state = new TextBlock { Foreground = (Brush)FindResource("MoonGold"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _tweakRowState[def.Id] = state;
            right.Children.Add(state);
            var open = new Button { Content = "Open Tweaks", Margin = new Thickness(0) };
            open.Click += (_, _) => { foreach (RadioButton rb in NavItems.Children) if ((string)rb.CommandParameter == "PageTweaks") rb.IsChecked = true; };
            right.Children.Add(open);
        }
        else
        {
            TextBox? box = null;
            if (def.Kind == FlagKind.Number)
            {
                box = new TextBox { Width = 76, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = def.Hint };
                box.TextChanged += (_, _) => FlagValueChanged(def, box);
                box.LostFocus += (_, _) => { bool was = _loading; _loading = true; box.Text = CurrentValue(def).ToString(); _loading = was; };
                right.Children.Add(new TextBlock { Text = def.Hint, Foreground = (Brush)FindResource("Dim"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                right.Children.Add(box);
            }
            var sw = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
            sw.Click += (_, _) => FlagSwitchChanged(def, sw.IsChecked == true);
            right.Children.Add(sw);
            _flagRows[def.Id] = (sw, box);
        }

        var text = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(new TextBlock { Text = def.Title, Style = (Style)FindResource("H2"), FontSize = 14 });
        text.Children.Add(new TextBlock { Text = def.Description, Style = (Style)FindResource("Note"), Margin = new Thickness(0, 4, 0, 0) });
        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", def.Keys), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11,
            Foreground = (Brush)FindResource("Dim"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        row.Children.Add(text);
        return row;
    }

    int CurrentValue(FlagDef def) => _s.FlagRows.TryGetValue(def.Id, out var v) ? v : def.Recommended;

    void FlagSwitchChanged(FlagDef def, bool on)
    {
        if (on)
        {
            int value = def.Kind == FlagKind.Toggle ? 1
                : _flagRows[def.Id].box is { } b && int.TryParse(b.Text, out var v) ? Math.Clamp(v, def.Min, def.Max) : def.Recommended;
            _s.FlagRows[def.Id] = value;
        }
        else _s.FlagRows.Remove(def.Id);
        RefreshFlagRows();
        SaveTweaks();
    }

    void FlagValueChanged(FlagDef def, TextBox box)
    {
        if (_loading || !_s.FlagRows.ContainsKey(def.Id)) return;
        if (!int.TryParse(box.Text, out var v) || v < def.Min || v > def.Max) return; // keep the last good value
        if (_s.FlagRows[def.Id] == v) return;
        _s.FlagRows[def.Id] = v;
        _s.Save();
    }

    void RefreshFlagRows()
    {
        bool was = _loading; _loading = true;
        foreach (var (id, (sw, box)) in _flagRows)
        {
            var def = FlagCatalog.Get(id)!;
            bool on = _s.FlagRows.ContainsKey(id);
            sw.IsChecked = on;
            if (box != null) { box.Text = CurrentValue(def).ToString(); box.IsEnabled = on; box.Opacity = on ? 1 : 0.45; }
        }
        if (_tweakRowState.TryGetValue("RenderApi", out var api)) api.Text = _s.RenderApi;
        if (_tweakRowState.TryGetValue("Msaa", out var msaa)) msaa.Text = _s.Msaa == 0 ? "Automatic" : $"{_s.Msaa}x";
        if (_tweakRowState.TryGetValue("Fullscreen", out var fs)) fs.Text = _s.ExclusiveFullscreen ? "On" : "Off";
        _loading = was;
    }

    void ApplyPreset(Dictionary<string, int> preset, string toast)
    {
        foreach (var (id, v) in preset) _s.FlagRows[id] = v;
        RefreshFlagRows();
        _s.Save();
        ShowToast(toast);
    }

    void PresetFps_Click(object sender, RoutedEventArgs e) => ApplyPreset(FlagCatalog.FpsBoost, "FPS Boost switched on");
    void PresetPotato_Click(object sender, RoutedEventArgs e) => ApplyPreset(FlagCatalog.MaxFps, "Max FPS switched on");
    void PresetClear_Click(object sender, RoutedEventArgs e)
    {
        _s.FlagRows.Clear();
        RefreshFlagRows();
        _s.Save();
        ShowToast("All flags off");
    }

    void SaveFlags_Click(object sender, RoutedEventArgs e)
    {
        try { Settings.SaveFlagsText(FlagsBox.Text); ShowToast("FastFlags saved"); }
        catch { ShowToast("Fix the JSON first", ok: false); }
    }

    // ---------------- home actions ----------------

    void Launch_Click(object sender, RoutedEventArgs e) => StartLaunch(Product.Player, LaunchBtn, LaunchText, "Launch Roblox");
    void LaunchStudio_Click(object sender, RoutedEventArgs e) => StartLaunch(Product.Studio, StudioBtn, StudioText, "Launch Studio");

    void StartLaunch(Product product, Button btn, TextBlock label, string idleText)
    {
        if (ValidateFlags()) Settings.SaveFlagsText(FlagsBox.Text);
        _s.Save();
        btn.IsEnabled = false;
        label.Text = "Launching...";
        _ = App.RunLaunchAsync(product, null, onLaunched: () =>
        {
            btn.IsEnabled = true;
            label.Text = idleText;
            _s = Settings.Load();
            RefreshStatus();
        });
    }

    // picks up account switches made inside Roblox when you come back to MoonStrap
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (IsLoaded && AccountService.ReadLocal()?.UserId != _account?.UserId) _ = LoadAccountAsync();
    }

    void Register_Click(object sender, RoutedEventArgs e)
    {
        bool wasRegistered = Protocol.IsRegistered && Protocol.IsStudioRegistered;
        if (wasRegistered) Protocol.Unregister(_s);
        else Protocol.Register(_s);
        RefreshStatus();
        ShowToast(wasRegistered ? "Previous launcher restored" : "MoonStrap is now your default launcher");
    }

    void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        if (Process.GetProcessesByName("RobloxPlayerBeta").Length > 0 || Process.GetProcessesByName("RobloxStudioBeta").Length > 0)
        {
            ShowToast("Close Roblox and Studio first", ok: false);
            return;
        }
        try
        {
            foreach (var d in Directory.GetDirectories(Paths.Versions)) Directory.Delete(d, true);
            _s.InstalledVersion = null;
            _s.InstalledStudioVersion = null;
            _s.LatestPlayerVersion = null;
            _s.LastPinnedVersion = null;
            _s.Save();
            RefreshStatus();
            ShowToast("Removed. It will re-download on next launch");
        }
        catch (Exception ex) { ShowToast(ex.Message, ok: false); }
    }

    void OpenInstall_Click(object sender, RoutedEventArgs e) =>
        Process.Start("explorer.exe", _s.InstalledVersion is { } v ? Deployment.VersionDir(v) : Paths.Versions);

    // ---------------- mods ----------------

    void RefreshMods()
    {
        var files = Directory.GetFiles(Paths.Mods, "*", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(Paths.Mods, f)).OrderBy(f => f).ToList();
        ModsList.ItemsSource = files;
        ModsHeader.Text = files.Count == 1 ? "1 mod installed" : $"{files.Count} mods installed";
        ModsEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

    void OpenMods_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", Paths.Mods);
}
