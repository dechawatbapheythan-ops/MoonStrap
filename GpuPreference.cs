using Microsoft.Win32;

namespace MoonStrap;

/// <summary>
/// On laptops with two GPUs, Windows chooses the GPU per-exe when the process starts, using
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences (same as Settings > Display > Graphics).
/// "GpuPreference=2;" = high performance (the dedicated NVIDIA/AMD card).
/// </summary>
public static class GpuPreference
{
    const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string HighPerformance = "GpuPreference=2;";

    /// <summary>Name of the dedicated GPU (e.g. "NVIDIA GeForce RTX 3050 Ti Laptop GPU"), or null.</summary>
    public static string? DedicatedGpuName()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is string desc &&
                    (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
                    return desc;
            }
        }
        catch { }
        return null;
    }

    public static void Set(string exePath, bool highPerformance)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (highPerformance) k.SetValue(exePath, HighPerformance);
            else if (k.GetValue(exePath) is string v && v == HighPerformance) k.DeleteValue(exePath, false);
        }
        catch { }
    }

    /// <summary>Applies the preference to MoonStrap itself and drops entries for exes that no longer exist
    /// (old MoonStrap Roblox versions, deleted Bloxstrap/Fishstrap installs).</summary>
    public static void ApplyToSelf(Settings s)
    {
        Set(Paths.Exe, s.UseDedicatedGpu);
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            if (k == null) return;
            foreach (var name in k.GetValueNames())
            {
                bool ours = name.StartsWith(Paths.Versions, StringComparison.OrdinalIgnoreCase);
                bool removedStrap = name.Contains(@"\Bloxstrap\", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains(@"\Fishstrap\", StringComparison.OrdinalIgnoreCase);
                if ((ours || removedStrap) && !File.Exists(name))
                    k.DeleteValue(name, false);
            }
        }
        catch { }
    }
}
