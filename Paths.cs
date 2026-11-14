namespace MoonStrap;

public static class Paths
{
    public static readonly string Base = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonStrap");
    public static readonly string Versions = Path.Combine(Base, "Versions");
    public static readonly string Downloads = Path.Combine(Base, "Downloads");
    public static readonly string Mods = Path.Combine(Base, "Modifications");
    public static readonly string ModBackups = Path.Combine(Base, "ModBackups");
    public static readonly string SettingsFile = Path.Combine(Base, "Settings.json");
    public static readonly string FlagsFile = Path.Combine(Base, "FastFlags.json");
    public static readonly string LogFile = Path.Combine(Base, "MoonStrap.log");
    public static string Exe => Environment.ProcessPath!;

    public static void Ensure()
    {
        foreach (var d in new[] { Base, Versions, Downloads, Mods, ModBackups })
            Directory.CreateDirectory(d);
    }
}
