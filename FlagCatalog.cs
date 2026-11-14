using System.Text.Json.Nodes;

namespace MoonStrap;

public enum FlagKind { Toggle, Number, Tweaks }

/// <summary>One row on the FastFlags page. A row can write several flags (e.g. texture override needs two).</summary>
public record FlagDef(
    string Id, string Group, string Title, string Description, FlagKind Kind,
    string[] Keys, int Recommended = 0, int Min = 0, int Max = 0, string? Hint = null)
{
    /// <summary>Flags this row writes when it's on, given the value from its number box.</summary>
    public IEnumerable<(string key, JsonNode value)> Write(int value) => Id switch
    {
        "TextureQuality" => [("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", value)],
        _ when Kind == FlagKind.Toggle => Keys.Select(k => (k, (JsonNode)"True")),
        _ => Keys.Select(k => (k, (JsonNode)value)),
    };
}

/// <summary>
/// Every flag on Roblox's client allowlist (DFStringAllowedPublicFlags, checked 2026-11-14).
/// Roblox ignores any flag that isn't on it.
/// </summary>
public static class FlagCatalog
{
    public static readonly FlagDef[] All =
    {
        // ---- graphics quality
        new("QualityLevel", "Graphics quality", "Force graphics level",
            "Locks Roblox's graphics quality to one level. 1 is the lowest and fastest, 21 the highest. The in-game graphics slider stops working while this is on.",
            FlagKind.Number, ["DFIntDebugFRMQualityLevelOverride"], Recommended: 1, Min: 1, Max: 21, Hint: "1 to 21"),
        new("TextureQuality", "Graphics quality", "Texture quality override",
            "Forces texture quality. 0 is the lowest (blurry, saves video memory), 3 the highest.",
            FlagKind.Number, ["DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride"], Recommended: 0, Min: 0, Max: 3, Hint: "0 to 3"),
        new("PauseVoxelizer", "Graphics quality", "Pause lighting voxelizer",
            "Stops Roblox from recalculating voxel lighting as the world changes. Saves CPU time, but lighting can look stuck or wrong.",
            FlagKind.Toggle, ["DFFlagDebugPauseVoxelizer"]),
        new("SkyGray", "Graphics quality", "Gray sky",
            "Replaces the skybox with flat gray. Tiny performance gain, mostly a look.",
            FlagKind.Toggle, ["FFlagDebugSkyGray"]),

        // ---- mesh detail
        new("LodDistance", "Mesh detail", "Level-of-detail distance",
            "How far away meshes and unions switch to simpler models. 0 makes them simplify right away for more FPS.",
            FlagKind.Number, ["DFIntCSGLevelOfDetailSwitchingDistance"], Recommended: 0, Min: 0, Max: 100000, Hint: "studs"),
        new("LodL12", "Mesh detail", "Detail level 1 → 2 distance",
            "Distance where meshes drop from full detail to the second level.",
            FlagKind.Number, ["DFIntCSGLevelOfDetailSwitchingDistanceL12"], Recommended: 0, Min: 0, Max: 100000, Hint: "studs"),
        new("LodL23", "Mesh detail", "Detail level 2 → 3 distance",
            "Distance where meshes drop from the second to the third level.",
            FlagKind.Number, ["DFIntCSGLevelOfDetailSwitchingDistanceL23"], Recommended: 0, Min: 0, Max: 100000, Hint: "studs"),
        new("LodL34", "Mesh detail", "Detail level 3 → 4 distance",
            "Distance where meshes drop to the simplest level.",
            FlagKind.Number, ["DFIntCSGLevelOfDetailSwitchingDistanceL34"], Recommended: 0, Min: 0, Max: 100000, Hint: "studs"),

        // ---- grass
        new("GrassMin", "Grass", "Grass start distance",
            "Distance where terrain grass starts fading out. Set both grass distances to 0 to remove grass.",
            FlagKind.Number, ["FIntFRMMinGrassDistance"], Recommended: 0, Min: 0, Max: 10000, Hint: "studs"),
        new("GrassMax", "Grass", "Grass end distance",
            "Distance where terrain grass is fully gone.",
            FlagKind.Number, ["FIntFRMMaxGrassDistance"], Recommended: 0, Min: 0, Max: 10000, Hint: "studs"),
        new("GrassMotion", "Grass", "Grass sway",
            "How much grass moves in the wind. 0 stops it moving.",
            FlagKind.Number, ["FIntGrassMovementReducedMotionFactor"], Recommended: 0, Min: 0, Max: 100, Hint: "0 = still"),

        // ---- display
        new("NoDpiScale", "Display", "Disable DPI scaling",
            "Renders Roblox at your screen's real resolution instead of scaling up for Windows display scaling. Sharper, but the menus look smaller.",
            FlagKind.Toggle, ["DFFlagDisableDPIScale"]),

        // ---- managed by the Tweaks tab
        new("RenderApi", "Set in Tweaks", "Rendering API",
            "Prefer Direct3D 11, Vulkan or OpenGL. Change it on the Tweaks tab.",
            FlagKind.Tweaks, ["FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL"]),
        new("Msaa", "Set in Tweaks", "Anti-aliasing (MSAA)",
            "Forces the number of anti-aliasing samples. Change it on the Tweaks tab.",
            FlagKind.Tweaks, ["FIntDebugForceMSAASamples"]),
        new("Fullscreen", "Set in Tweaks", "Exclusive fullscreen",
            "Alt+Enter uses real exclusive fullscreen. Change it on the Tweaks tab.",
            FlagKind.Tweaks, ["FFlagHandleAltEnterFullscreenManually"]),
    };

    public static FlagDef? Get(string id) => All.FirstOrDefault(d => d.Id == id);

    public static readonly Dictionary<string, int> FpsBoost = new()
    {
        ["LodDistance"] = 0, ["LodL12"] = 0, ["LodL23"] = 0, ["LodL34"] = 0, ["GrassMin"] = 0, ["GrassMax"] = 0,
    };

    public static readonly Dictionary<string, int> MaxFps = new(FpsBoost)
    {
        ["QualityLevel"] = 1, ["TextureQuality"] = 0, ["PauseVoxelizer"] = 1, ["SkyGray"] = 1, ["GrassMotion"] = 0,
    };
}
