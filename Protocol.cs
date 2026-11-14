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
    public static bool IsStudioRegistered => IsOurs(CurrentCommand("roblox-studio"));

    public static void Register(Settings s)
    {
        foreach (var scheme in AllSchemes)
        {
            var existing = CurrentCommand(scheme);
            if (existing != null && !IsOurs(existing))
                s.OriginalProtocolCommands[scheme] = existing;

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
        s.Save();
    }

    public static void Unregister(Settings s)
    {
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
