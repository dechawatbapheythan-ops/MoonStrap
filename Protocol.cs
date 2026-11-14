using Microsoft.Win32;

namespace MoonStrap;

/// <summary>
/// Registers MoonStrap as the handler for Roblox website links:
/// roblox-player: / roblox: (Play buttons) and roblox-studio: / roblox-studio-auth: (Edit in Studio).
/// The previous handler for each link type is remembered so it can be restored.
/// </summary>
public static class Protocol
{
    static readonly string[] PlayerSchemes = { "roblox-player", "roblox" };
    static readonly string[] StudioSchemes = { "roblox-studio", "roblox-studio-auth" };
    static IEnumerable<string> AllSchemes => PlayerSchemes.Concat(StudioSchemes);

    static string OurCommand => $"\"{Paths.Exe}\" \"%1\"";

    public static string? CurrentCommand(string scheme = "roblox-player")
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command");
        return k?.GetValue("") as string;
    }

    static bool IsOurs(string? cmd) =>
        cmd != null && (cmd.Contains(Paths.Exe, StringComparison.OrdinalIgnoreCase) ||
                        cmd.Contains("MoonStrap.exe", StringComparison.OrdinalIgnoreCase));

    public static bool IsRegistered => IsOurs(CurrentCommand("roblox-player"));
    /// <summary>Play links open this exact exe (not just some MoonStrap.exe).</summary>
    public static bool PointsHere => CurrentCommand("roblox-player") == OurCommand;
    public static bool IsStudioRegistered => IsOurs(CurrentCommand("roblox-studio"));

    /// <summary>True when any Roblox link or place-file type no longer opens MoonStrap.</summary>
    public static bool NeedsReclaim()
    {
        if (AllSchemes.Any(sc => CurrentCommand(sc) != OurCommand)) return true;
        using var place = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Roblox.Place\shell\open\command");
        if (place?.GetValue("") as string != OurCommand) return true;
        foreach (var ext in new[] { ".rbxl", ".rbxlx" })
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}");
            if (k?.GetValue("") as string != "Roblox.Place") return true;
        }
        return false;
    }

    /// <summary>Points every Roblox link back at MoonStrap without touching Settings (used by the link guard).</summary>
    public static void Reclaim() => Write();

    public static void Register(Settings s)
    {
        foreach (var scheme in AllSchemes)
        {
            var existing = CurrentCommand(scheme);
            if (existing != null && !IsOurs(existing))
                s.OriginalProtocolCommands[scheme] = existing;
        }
        Write();
        s.Save();
        LinkGuard.Enabled = true;
    }

    static void Write()
    {
        foreach (var scheme in AllSchemes)
        {
            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
            root.SetValue("", $"URL: {scheme} Protocol");
            root.SetValue("URL Protocol", "");
            using var icon = root.CreateSubKey("DefaultIcon");
            icon.SetValue("", Paths.Exe);
            using var cmd = root.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", OurCommand);
        }
        // .rbxl / .rbxlx place files -> open in Studio through MoonStrap
        using (var place = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Roblox.Place"))
        {
            place.SetValue("", "Roblox Place");
            using var icon = place.CreateSubKey("DefaultIcon");
            icon.SetValue("", Paths.Exe + ",0");
            using var cmd = place.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", OurCommand);
        }
        foreach (var ext in new[] { ".rbxl", ".rbxlx" })
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            k.SetValue("", "Roblox.Place");
        }
    }

    public static void Unregister(Settings s)
    {
        LinkGuard.Enabled = false; // first, or the guard would put the links straight back
        foreach (var scheme in AllSchemes)
        {
            if (!IsOurs(CurrentCommand(scheme))) continue; // someone else owns it, leave it

            string? original = s.OriginalProtocolCommands.GetValueOrDefault(scheme)
                ?? (PlayerSchemes.Contains(scheme) ? s.OriginalProtocolCommand : null);
            if (original != null)
            {
                using var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}\shell\open\command");
                cmd.SetValue("", original);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{scheme}", false);
            }
        }
    }
}
