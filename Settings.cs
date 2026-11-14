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

    /// <summary>FastFlags page rows that are switched on: row id -> value (1 for plain on/off rows).</summary>
    public Dictionary<string, int> FlagRows { get; set; } = new();

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

    /// <summary>Moves flags that now have their own row on the FastFlags page out of the custom JSON
    /// (e.g. ones added by the old preset buttons) and switches those rows on instead.</summary>
    public bool MigrateCustomFlags()
    {
        JsonObject custom;
        try { custom = JsonNode.Parse(LoadFlagsText()) as JsonObject ?? new(); }
        catch { return false; }
        bool changed = false;
        foreach (var def in FlagCatalog.All.Where(d => d.Kind != FlagKind.Tweaks))
        {
            var valueKey = def.Keys[^1];
            if (custom[valueKey] is not JsonNode node) continue;
            var text = node.ToString();
            if (def.Kind == FlagKind.Toggle)
            {
                // "False" is the same as the row being off
                if (text.Equals("True", StringComparison.OrdinalIgnoreCase)) FlagRows.TryAdd(def.Id, 1);
            }
            else FlagRows.TryAdd(def.Id, int.TryParse(text, out var v) ? Math.Clamp(v, def.Min, def.Max) : def.Recommended);
            foreach (var k in def.Keys) custom.Remove(k);
            changed = true;
        }
        // The old Max FPS preset also put MSAA in the custom JSON; that now lives on the Tweaks tab.
        if (custom["FIntDebugForceMSAASamples"] is JsonNode msaa)
        {
            if (Msaa == 0 && int.TryParse(msaa.ToString(), out var m) && m is 1 or 2 or 4 or 8) Msaa = m;
            custom.Remove("FIntDebugForceMSAASamples");
            changed = true;
        }
        if (changed) File.WriteAllText(Paths.FlagsFile, custom.ToJsonString(Opts));
        return changed;
    }

    /// <summary>Custom flags, then the FastFlags page rows, then the Tweaks tab (later ones win).</summary>
    public JsonObject BuildFlags()
    {
        JsonObject flags;
        try { flags = JsonNode.Parse(LoadFlagsText()) as JsonObject ?? new(); }
        catch { flags = new(); }

        foreach (var (id, value) in FlagRows)
            if (FlagCatalog.Get(id) is { Kind: not FlagKind.Tweaks } def)
                foreach (var (k, v) in def.Write(value)) flags[k] = v;

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
