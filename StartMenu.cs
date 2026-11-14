namespace MoonStrap;

/// <summary>
/// Keeps a Start Menu shortcut pointing at the running exe, which is what makes
/// MoonStrap show up in Windows Search.
/// </summary>
public static class StartMenu
{
    static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MoonStrap.lnk");

    public static void Ensure()
    {
        // only the real MoonStrap.exe may own the shortcut (not dotnet.exe, test harnesses, etc.)
        if (!string.Equals(Path.GetFileName(Paths.Exe), "MoonStrap.exe", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;

            if (File.Exists(ShortcutPath))
            {
                dynamic existing = shell.CreateShortcut(ShortcutPath);
                if (string.Equals((string)existing.TargetPath, Paths.Exe, StringComparison.OrdinalIgnoreCase))
                    return; // already correct
            }

            dynamic lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = Paths.Exe;
            lnk.WorkingDirectory = Path.GetDirectoryName(Paths.Exe);
            lnk.IconLocation = Paths.Exe + ",0";
            lnk.Description = "MoonStrap - moon-themed Roblox launcher";
            lnk.Save();
        }
        catch { /* never block startup over a shortcut */ }
    }
}
