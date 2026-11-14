using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoonStrap;

public class Settings
{
    public int FpsCap { get; set; } = 0;                 // 0 = Roblox default
    public string RenderApi { get; set; } = "Automatic"; // Automatic | Direct3D 11 | Vulkan | OpenGL
    public bool ExclusiveFullscreen { get; set; } = false;
    public bool MultiInstance { get; set; } = false;
    public string? InstalledStudioVersion { get; set; }
    public Dictionary<string, string> OriginalProtocolCommands { get; set; } = new();

    public string? GetInstalled(Product p) => p == Product.Studio ? InstalledStudioVersion : InstalledVersion;
    public void SetInstalled(Product p, string? v)
    {
        if (p == Product.Studio) InstalledStudioVersion = v; else InstalledVersion = v;
    }

    public bool UseDedicatedGpu { get; set; } = true;     // run MoonStrap + Roblox on the NVIDIA card
    public int Msaa { get; set; } = 0;                 // 0 = automatic, otherwise 1/2/4/8 samples
    public string? PinnedVersion { get; set; }           // null = always use latest
    public string? PinnedLabel { get; set; }             // e.g. "0.712.0.7120913"
    public string? LastPinnedVersion { get; set; }       // older build kept on disk after going back to latest
    public string? LatestPlayerVersion { get; set; }     // newest auto-updated build, kept while pinned
    public string? InstalledVersion { get; set; }
    public string? OriginalProtocolCommand { get; set; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new(); }
        catch { s = new(); }
        s.FpsCap = Math.Clamp(s.FpsCap, 0, 9999);
        // settings from before LatestPlayerVersion existed: an unpinned install is the latest build
        if (s.LatestPlayerVersion == null && s.PinnedVersion == null && s.InstalledVersion != null && s.InstalledVersion != s.LastPinnedVersion)
            s.LatestPlayerVersion = s.InstalledVersion;
        return s;
    }

    public void Save() => File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, Opts));

    // ---- Custom FastFlags (raw JSON object the user edits) ----
    public static string LoadFlagsText() =>
        File.Exists(Paths.FlagsFile) ? File.ReadAllText(Paths.FlagsFile) : "{\n}";

    public static void SaveFlagsText(string text)
    {
        // throws if invalid, caller shows the error
        var obj = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("FastFlags must be a JSON object { ... }");
        File.WriteAllText(Paths.FlagsFile, obj.ToJsonString(Opts));
    }

    /// <summary>Custom flags merged with the presets from the Tweaks tab (presets win).</summary>
    public JsonObject BuildFlags()
    {
        JsonObject flags;
        try { flags = JsonNode.Parse(LoadFlagsText()) as JsonObject ?? new(); }
        catch { flags = new(); }

        if (FpsCap > 0) flags["DFIntTaskSchedulerTargetFps"] = FpsCap;

        foreach (var k in new[] { "FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL" })
            if (RenderApi != "Automatic") flags.Remove(k);
        switch (RenderApi)
        {
            case "Direct3D 11": flags["FFlagDebugGraphicsPreferD3D11"] = "True"; break;
            case "Vulkan": flags["FFlagDebugGraphicsPreferVulkan"] = "True"; break;
            case "OpenGL": flags["FFlagDebugGraphicsPreferOpenGL"] = "True"; break;
        }

        if (ExclusiveFullscreen) flags["FFlagHandleAltEnterFullscreenManually"] = "False";
        if (Msaa > 0) flags["FIntDebugForceMSAASamples"] = Msaa;
        return flags;
    }
}
