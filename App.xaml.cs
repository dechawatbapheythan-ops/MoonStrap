using System.Windows;

namespace MoonStrap;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Paths.Ensure();
        StartMenu.Ensure();
        GpuPreference.ApplyToSelf(Settings.Load());
        var arg = e.Args.FirstOrDefault();

        // one-off version for this launch only: MoonStrap.exe -player -version <latest | version-hash | 0.710[.0.7100702]>
        string? versionOverride = null;
        int vi = Array.FindIndex(e.Args, a => a.Equals("-version", StringComparison.OrdinalIgnoreCase));
        if (vi >= 0 && vi + 1 < e.Args.Length) versionOverride = e.Args[vi + 1];
        bool Starts(string prefix) => arg?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;

        bool playerLink = Starts("roblox-player:") || Starts("roblox:");
        // double-clicked .rbxl / .rbxlx place files open in Studio
        bool placeFile = arg != null && File.Exists(arg) &&
            (arg.EndsWith(".rbxl", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".rbxlx", StringComparison.OrdinalIgnoreCase));
        bool studioLink = Starts("roblox-studio:") || Starts("roblox-studio-auth:") || placeFile;

        if (playerLink || studioLink || arg == "-player" || arg == "-studio")
        {
            var product = studioLink || arg == "-studio" ? Product.Studio : Product.Player;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await RunLaunchAsync(product, playerLink || studioLink ? arg : null, versionOverride: versionOverride);
            Shutdown();
        }
        else
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }

    /// <summary>Install/update Roblox, apply tweaks, launch. Used by protocol links and the Launch button.</summary>
    public static async Task RunLaunchAsync(Product product, string? protocolUri, Action? onLaunched = null, string? versionOverride = null)
    {
        var settings = Settings.Load();
        var name = Deployment.DisplayName(product);
        var window = new BootstrapperWindow(name);
        window.Show();
        try
        {
            var progress = new Progress<(string status, double pct)>(p => window.SetStatus(p.status, p.pct));
            if (versionOverride != null && product == Product.Player)
            {
                window.SetStatus($"Finding Roblox {versionOverride}...", -1);
                versionOverride = await Deployment.ResolveVersionAsync(versionOverride, window.Cancellation.Token);
            }
            var dir = await Deployment.EnsureLatestAsync(settings, product, progress, window.Cancellation.Token,
                product == Product.Player ? versionOverride : null);

            window.SetStatus("Applying tweaks...", -1);
            Launcher.ApplyFastFlags(dir, settings);
            Launcher.ApplyMods(dir);
            GpuPreference.Set(Path.Combine(dir, Deployment.ExeName(product)), settings.UseDedicatedGpu);

            window.SetStatus($"Launching {name}...", -1);
            var proc = Launcher.Launch(product, dir, protocolUri, settings);

            // keep the loader up until Roblox's own window is actually on screen
            window.SetStatus($"Waiting for {name} to open...", -1);
            bool opened;
            try { opened = await Launcher.WaitForWindowAsync(proc, product, window.Cancellation.Token, TimeSpan.FromSeconds(90)); }
            catch (OperationCanceledException) { opened = true; } // Cancel here just hides the loader; Roblox keeps starting

            if (!opened)
            {
                window.Close();
                onLaunched?.Invoke();
                MessageBox.Show($"{name} closed before it finished opening (exit code {SafeExitCode(proc)}).\n\nTry launching again. If it keeps happening, use Reinstall on the Home page.",
                    "MoonStrap", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await window.FadeOutAndCloseAsync();
            onLaunched?.Invoke();

            if (product == Product.Player && settings.MultiInstance)
                await Launcher.WaitForAllClientsAsync();
        }
        catch (OperationCanceledException) { window.Close(); onLaunched?.Invoke(); }
        catch (Exception ex)
        {
            Log(ex);
            window.Close();
            onLaunched?.Invoke();
            MessageBox.Show($"MoonStrap could not start {name}:\n\n{ex.Message}\n\nDetails were saved to {Paths.LogFile}",
                "MoonStrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static string SafeExitCode(System.Diagnostics.Process p)
    {
        try { return p.ExitCode.ToString(); } catch { return "unknown"; }
    }

    static void Log(Exception ex)
    {
        try { File.AppendAllText(Paths.LogFile, $"[{DateTime.Now:u}] {ex}\n\n"); } catch { }
    }
}
