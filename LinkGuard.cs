using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MoonStrap;

/// <summary>
/// Keeps Roblox website links pointed at MoonStrap. The official Roblox/Studio installers, Bloxstrap
/// and similar launchers re-register themselves when they run or update; the guard notices the
/// registry change instantly (RegNotifyChangeKeyValue, no polling) and points the links back.
/// Runs as a tiny background process (`MoonStrap.exe -guard`) started at Windows sign-in.
/// </summary>
public static class LinkGuard
{
    const string StateKey = @"Software\MoonStrap";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "MoonStrap Link Guard";
    const string MutexName = @"Local\MoonStrapLinkGuard";
    public const string Arg = "-guard";

    static string RunCommand => $"\"{Paths.Exe}\" {Arg}";

    /// <summary>null until the user (or the first start of this version) has decided.</summary>
    static bool? State
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(StateKey);
            return k?.GetValue("KeepLinks") is int v ? v == 1 : null;
        }
    }

    /// <summary>True while the user wants MoonStrap to own Roblox links (set by Make default / Restore).</summary>
    public static bool Enabled
    {
        get => State == true;
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(StateKey);
            k.SetValue("KeepLinks", value ? 1 : 0, RegistryValueKind.DWord);
            if (value)
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                run.SetValue(RunName, RunCommand);
                StartBackground();
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(RunName, throwOnMissingValue: false);
                // the running guard sees KeepLinks=0 when this wakes it, and exits
                k.SetValue("Poke", Environment.TickCount);
            }
        }
    }

    /// <summary>
    /// Called on every normal MoonStrap start: re-takes the links, keeps the sign-in entry pointing at
    /// this exe and the guard alive. Leaves things alone if another MoonStrap.exe copy owns the guard
    /// (e.g. a test build in bin\ next to the installed one).
    /// </summary>
    public static void EnsureRunning()
    {
        if (!string.Equals(Path.GetFileName(Paths.Exe), "MoonStrap.exe", StringComparison.OrdinalIgnoreCase)) return;
        switch (State)
        {
            case null: // first start after updating from a version without the guard
                if (Protocol.PointsHere) Enabled = true;
                return;
            case false:
                return;
        }
        string? owner;
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            owner = (run?.GetValue(RunName) as string)?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (owner != null && !owner.Equals(Paths.Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(owner)) return;

        if (Protocol.NeedsReclaim()) Protocol.Reclaim();
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            if (run.GetValue(RunName) as string != RunCommand) run.SetValue(RunName, RunCommand);
        StartBackground();
    }

    static void StartBackground()
    {
        if (Mutex.TryOpenExisting(MutexName, out var m)) { m.Dispose(); return; } // already running
        try { Process.Start(new ProcessStartInfo(Paths.Exe, Arg) { UseShellExecute = false }); } catch { }
    }

    /// <summary>Entry point for `MoonStrap.exe -guard`. Blocks until the guard is switched off.</summary>
    public static void Run()
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first) return;

        // reclaims in the last minute, so two programs fighting over the links can't spin forever
        var recent = new Queue<DateTime>();
        using var classesChanged = new AutoResetEvent(false);
        using var stateChanged = new AutoResetEvent(false);
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes")!;
        using var state = Registry.CurrentUser.CreateSubKey(StateKey);
        const int Flags = 0x1 | 0x4; // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET

        // notifications are one-shot: arm both now, then re-arm only the one that fired
        bool Arm(RegistryKey key, bool subtree, AutoResetEvent ev) =>
            RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), subtree, Flags, ev.SafeWaitHandle.DangerousGetHandle(), true) == 0;
        bool armed = Arm(classes, true, classesChanged) & Arm(state, false, stateChanged);
        GC.Collect();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); // idle: give startup memory back

        while (Enabled)
        {
            if (Protocol.NeedsReclaim())
            {
                while (recent.Count > 0 && DateTime.Now - recent.Peek() > TimeSpan.FromMinutes(1)) recent.Dequeue();
                if (recent.Count < 10)
                {
                    recent.Enqueue(DateTime.Now);
                    try { Protocol.Reclaim(); Log("Roblox links were taken over by another program; pointed them back at MoonStrap."); }
                    catch (Exception ex) { Log("Couldn't reclaim Roblox links: " + ex.Message); }
                }
            }

            // without notifications (shouldn't happen) fall back to a slow check
            int fired = WaitHandle.WaitAny([classesChanged, stateChanged], armed ? Timeout.Infinite : 30_000);
            if (fired == 0) armed = Arm(classes, true, classesChanged);
            else if (fired == 1) armed = Arm(state, false, stateChanged);
            Thread.Sleep(300); // let the other program finish writing before we look
        }
    }

    static void Log(string msg)
    {
        try { File.AppendAllText(Paths.LogFile, $"[{DateTime.Now:u}] {msg}\n"); } catch { }
    }

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    [DllImport("advapi32.dll")]
    static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree, int notifyFilter, IntPtr hEvent, bool asynchronous);
}
