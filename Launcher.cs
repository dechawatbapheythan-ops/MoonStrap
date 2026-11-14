using System.Diagnostics;
using System.Text.Json;

namespace MoonStrap;

public static class Launcher
{
    static Mutex? _singletonMutex;

    public static void ApplyFastFlags(string versionDir, Settings s)
    {
        var dir = Path.Combine(versionDir, "ClientSettings");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ClientAppSettings.json"),
            s.BuildFlags().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Copies everything in the Modifications folder over the Roblox install.
    /// Originals are backed up, and restored when you delete a mod file.
    /// </summary>
    public static void ApplyMods(string versionDir)
    {
        var backupRoot = Path.Combine(Paths.ModBackups, Path.GetFileName(versionDir));

        // restore originals for mods that were removed
        if (Directory.Exists(backupRoot))
        {
            foreach (var backup in Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(backupRoot, backup);
                if (!File.Exists(Path.Combine(Paths.Mods, rel)))
                {
                    File.Copy(backup, Path.Combine(versionDir, rel), true);
                    File.Delete(backup);
                }
            }
        }

        foreach (var mod in Directory.GetFiles(Paths.Mods, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Paths.Mods, mod);
            var target = Path.Combine(versionDir, rel);
            var backup = Path.Combine(backupRoot, rel);
            if (File.Exists(target) && !File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(mod, target, true);
        }
    }

    public static Process Launch(Product product, string versionDir, string? protocolUri, Settings s)
    {
        var psi = new ProcessStartInfo(Path.Combine(versionDir, Deployment.ExeName(product)))
        {
            WorkingDirectory = versionDir,
            UseShellExecute = false,
        };

        if (product == Product.Studio)
        {
            // No link: open Studio's start page.
            // roblox-studio-auth: (login handoff) is passed as-is; roblox-studio: (Edit button) via -protocolString.
            if (!string.IsNullOrWhiteSpace(protocolUri))
            {
                if (protocolUri.StartsWith("roblox-studio-auth:", StringComparison.OrdinalIgnoreCase) || File.Exists(protocolUri))
                    psi.ArgumentList.Add(protocolUri);
                else
                {
                    psi.ArgumentList.Add("-protocolString");
                    psi.ArgumentList.Add(protocolUri);
                }
            }
        }
        else
        {
            if (s.MultiInstance)
            {
                // Roblox refuses to open a second client when this mutex is already held.
                // Holding it ourselves lets every client start.
                _singletonMutex ??= new Mutex(true, "ROBLOX_singletonMutex");
            }
            psi.ArgumentList.Add(string.IsNullOrWhiteSpace(protocolUri) ? "--app" : protocolUri);
        }

        return Process.Start(psi) ?? throw new InvalidOperationException($"{Deployment.DisplayName(product)} failed to start.");
    }

    /// <summary>
    /// Waits until Roblox (or Studio) has put its window on screen, so the loading UI stays up the whole time.
    /// Roblox sometimes hands off to a second process, so any matching process started after launch counts.
    /// Returns false if Roblox exited without ever showing a window.
    /// </summary>
    public static async Task<bool> WaitForWindowAsync(Process proc, Product product, CancellationToken ct, TimeSpan timeout)
    {
        var exe = Path.GetFileNameWithoutExtension(Deployment.ExeName(product));
        var started = DateTime.Now.AddSeconds(-2);
        var deadline = DateTime.Now + timeout;

        while (DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var p in Process.GetProcessesByName(exe))
            {
                try
                {
                    if (p.Id != proc.Id && p.StartTime < started) continue; // an older client, not ours
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle)) return true;
                }
                catch { /* exited or access denied */ }
                finally { if (p.Id != proc.Id) p.Dispose(); }
            }
            if (proc.HasExited && Process.GetProcessesByName(exe).All(p => { try { return p.StartTime < started; } catch { return true; } }))
                return false;
            await Task.Delay(250, ct);
        }
        return true; // timed out: Roblox is still starting; don't keep the loader up forever
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>With multi-instance on we must keep the mutex alive until all clients close.</summary>
    public static async Task WaitForAllClientsAsync()
    {
        while (Process.GetProcessesByName("RobloxPlayerBeta").Length > 0)
            await Task.Delay(3000);
    }
}
