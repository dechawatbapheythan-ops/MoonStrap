using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MoonStrap;

public enum Product { Player, Studio }

/// <summary>Downloads and installs Roblox (player or Studio) from Roblox's own CDN into our own folder.</summary>
public static class Deployment
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    const string CdnBase = "https://setup.rbxcdn.com/";

    static string VersionUrl(Product p) => p == Product.Studio
        ? "https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64"
        : "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";

    public static string ExeName(Product p) => p == Product.Studio ? "RobloxStudioBeta.exe" : "RobloxPlayerBeta.exe";
    public static string DisplayName(Product p) => p == Product.Studio ? "Roblox Studio" : "Roblox";

    static Dictionary<string, string> MapFor(Product p) => p == Product.Studio ? StudioMap : PlayerMap;

    // Studio = everything the player has, plus its own editor packages.
    static Dictionary<string, string>? _studioMap;
    static Dictionary<string, string> StudioMap => _studioMap ??= new(PlayerMap, StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxStudio.zip"] = "",
        ["Libraries.zip"] = "",
        ["LibrariesQt5.zip"] = "",
        ["ApplicationConfig.zip"] = @"ApplicationConfig\",
        ["BuiltInPlugins.zip"] = @"BuiltInPlugins\",
        ["BuiltInStandalonePlugins.zip"] = @"BuiltInStandalonePlugins\",
        ["Plugins.zip"] = @"Plugins\",
        ["RibbonConfig.zip"] = @"RibbonConfig\",
        ["StudioFonts.zip"] = @"StudioFonts\",
        ["content-api-docs.zip"] = @"content\api_docs\",
        ["content-qt_translations.zip"] = @"content\qt_translations\",
        ["content-studio_svg_textures.zip"] = @"content\studio_svg_textures\",
        ["extracontent-scripts.zip"] = @"ExtraContent\scripts\",
        ["studiocontent-models.zip"] = @"StudioContent\models\",
        ["studiocontent-textures.zip"] = @"StudioContent\textures\",
    };

    // Where each package zip gets extracted, relative to the version folder.
    static readonly Dictionary<string, string> PlayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["redist.zip"] = "",
        ["WebView2.zip"] = "",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",
        ["WebView2RuntimeInstaller.zip"] = @"WebView2RuntimeInstaller\",
        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };

    const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<ContentFolder>content</ContentFolder>\r\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n";

    record Package(string Name, string Md5, long PackedSize);

    public static async Task<string> GetLatestVersionAsync(Product product, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(VersionUrl(product), ct));
        return doc.RootElement.GetProperty("clientVersionUpload").GetString()!;
    }

    public record HistoryEntry(string Hash, DateTime Date, string Version)
    {
        public string DateText => Date.ToString("MMM d, yyyy");
    }

    static readonly System.Text.RegularExpressions.Regex HistoryLine = new(
        @"New WindowsPlayer (version-[0-9a-f]{16}) at ([^,]+), file version: (\d+), (\d+), (\d+), (\d+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Past Windows player builds from Roblox's public DeployHistory.txt, newest first.
    /// Roblox hides the IDs of recent builds ("version-hidden"), so those can't be listed.
    /// </summary>
    public static async Task<List<HistoryEntry>> GetHistoryAsync(CancellationToken ct = default)
    {
        var text = await Http.GetStringAsync($"{CdnBase}DeployHistory.txt", ct);
        var seen = new HashSet<string>();
        var list = new List<HistoryEntry>();
        foreach (System.Text.RegularExpressions.Match m in HistoryLine.Matches(text))
        {
            if (!seen.Add(m.Groups[1].Value)) continue;
            DateTime.TryParse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date);
            list.Add(new HistoryEntry(m.Groups[1].Value, date,
                $"{m.Groups[3].Value}.{m.Groups[4].Value}.{m.Groups[5].Value}.{m.Groups[6].Value}"));
        }
        list.Reverse();
        return list;
    }

    /// <summary>Player builds installed in MoonStrap's Versions folder, newest first, with their version numbers.</summary>
    public static List<(string Hash, string Version)> InstalledPlayerBuilds()
    {
        var list = new List<(string Hash, string Version)>();
        foreach (var dir in Directory.GetDirectories(Paths.Versions))
        {
            var exe = Path.Combine(dir, ExeName(Product.Player));
            if (!File.Exists(Marker(dir)) || !File.Exists(exe)) continue;
            // use the FileVersion *string*: Roblox's last part (e.g. 7100702) overflows the 16-bit numeric fields
            var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
            list.Add((Path.GetFileName(dir), string.Join(".", fv.Split(',').Select(p => p.Trim()))));
        }
        return list.OrderByDescending(b => Version.TryParse(b.Version, out var v) ? v : new Version()).ToList();
    }

    /// <summary>
    /// Turns "latest", "version-xxxxxxxxxxxxxxxx", "0.710.0.7100702" or "0.710" into "latest" or a version hash
    /// that Roblox's servers still host. A partial number picks the newest matching build.
    /// </summary>
    public static async Task<string> ResolveVersionAsync(string input, CancellationToken ct = default)
    {
        input = input.Trim();
        if (input.Equals("latest", StringComparison.OrdinalIgnoreCase)) return "latest";

        string hash;
        if (System.Text.RegularExpressions.Regex.IsMatch(input, "^version-[0-9a-fA-F]{16}$"))
            hash = input.ToLowerInvariant();
        else if (InstalledPlayerBuilds().FirstOrDefault(b => b.Version == input || b.Version.StartsWith(input + ".", StringComparison.Ordinal))
                 is { Hash: not null } local)
        {
            hash = local.Hash; // already on disk (covers recent builds Roblox hides from its public history)
        }
        else
        {
            var history = await GetHistoryAsync(ct);
            var match = history.FirstOrDefault(h => h.Version == input)
                     ?? history.FirstOrDefault(h => h.Version.StartsWith(input + ".", StringComparison.Ordinal));
            hash = match?.Hash ?? throw new InvalidOperationException(
                $"No Roblox build matches \"{input}\". Use a number like 0.710 or 0.710.0.7100702, or a version-… ID.");
        }

        // already installed locally? no need to ask the CDN
        if (File.Exists(Marker(VersionDir(hash))) || await VersionExistsAsync(hash, ct)) return hash;
        throw new InvalidOperationException($"Roblox's servers no longer host {hash}.");
    }

    /// <summary>True if Roblox's CDN still hosts this build.</summary>
    public static async Task<bool> VersionExistsAsync(string hash, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync($"{CdnBase}{hash}-rbxPkgManifest.txt", HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }

    public static string VersionDir(string version) => Path.Combine(Paths.Versions, version);
    static string Marker(string dir) => Path.Combine(dir, ".moonstrap-complete");

    /// <summary>Makes sure the latest (or pinned) build of a product is installed; returns its folder.</summary>
    /// <param name="versionOverride">Player only, one launch only: "latest" or a version hash. Doesn't change the saved pin.</param>
    public static async Task<string> EnsureLatestAsync(Settings settings, Product product, IProgress<(string, double)> progress,
        CancellationToken ct, string? versionOverride = null)
    {
        string name = DisplayName(product);
        string? installed = settings.GetInstalled(product);
        string version;
        bool forceLatest = versionOverride == "latest";
        if (versionOverride != null && !forceLatest)
        {
            version = versionOverride;
        }
        else if (!forceLatest && product == Product.Player && settings.PinnedVersion is { } pinned)
        {
            version = pinned; // user chose an older build: never auto-update
        }
        else
        {
            progress.Report(($"Checking for {name} updates...", -1));
            try { version = await GetLatestVersionAsync(product, ct); }
            catch (Exception)
            {
                // offline: fall back to the newest build we already have
                var fallback = product == Product.Player && settings.LatestPlayerVersion is { } l && File.Exists(Marker(VersionDir(l)))
                    ? l : installed;
                if (fallback == null || !File.Exists(Marker(VersionDir(fallback)))) throw;
                if (installed != fallback) { settings.SetInstalled(product, fallback); settings.Save(); }
                return VersionDir(fallback);
            }
            if (product == Product.Player) settings.LatestPlayerVersion = version;
        }

        var dir = VersionDir(version);
        if (File.Exists(Marker(dir)))
        {
            // already on disk (e.g. switching between latest and a pinned build): no download
            if (installed != version) settings.SetInstalled(product, version);
            settings.Save();
            return dir;
        }
        var PackageMap = MapFor(product);

        progress.Report(($"Fetching package manifest for {version}...", -1));
        var packages = ParseManifest(await Http.GetStringAsync($"{CdnBase}{version}-rbxPkgManifest.txt", ct));

        var staging = dir + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        long total = packages.Sum(p => p.PackedSize), done = 0;
        foreach (var pkg in packages)
        {
            var zip = Path.Combine(Paths.Downloads, pkg.Md5);
            if (!File.Exists(zip) || Md5(zip) != pkg.Md5)
            {
                await DownloadAsync($"{CdnBase}{version}-{pkg.Name}", zip, b =>
                    progress.Report(($"Downloading {name}: {pkg.Name}", (done + b) / (double)total)), ct);
                if (Md5(zip) != pkg.Md5)
                {
                    File.Delete(zip);
                    throw new InvalidDataException($"Checksum mismatch on {pkg.Name}. Try again.");
                }
            }
            done += pkg.PackedSize;

            progress.Report(($"Installing {pkg.Name}...", done / (double)total));
            var dest = Path.Combine(staging, PackageMap.GetValueOrDefault(pkg.Name, ""));
            await Task.Run(() => Extract(zip, dest), ct);
        }

        File.WriteAllText(Path.Combine(staging, "AppSettings.xml"), AppSettingsXml);
        File.WriteAllText(Marker(staging), version);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(staging, dir);
        foreach (var pkg in packages)
            try { File.Delete(Path.Combine(Paths.Downloads, pkg.Md5)); } catch { }

        settings.SetInstalled(product, version);
        settings.Save();
        // keep: what's running now, Studio, the latest player build, and the older build you last pinned,
        // so switching between "latest" and "pinned" never re-downloads
        Cleanup(settings.InstalledVersion, settings.InstalledStudioVersion,
                settings.LatestPlayerVersion, settings.PinnedVersion ?? settings.LastPinnedVersion);
        return dir;
    }

    static List<Package> ParseManifest(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "v0") throw new InvalidDataException("Unknown package manifest format.");
        var list = new List<Package>();
        // after "v0": groups of 4 lines -> name, md5, packed size, unpacked size
        for (int i = 1; i + 3 < lines.Length; i += 4)
        {
            var name = lines[i];
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                list.Add(new Package(name, lines[i + 1].ToLowerInvariant(), long.Parse(lines[i + 2])));
        }
        return list;
    }

    /// <summary>
    /// Roblox's zips use backslash paths and contain a bare "\" root entry, which .NET's
    /// ZipFile.ExtractToDirectory rejects as path traversal. Extract entry-by-entry instead.
    /// </summary>
    static void Extract(string zipPath, string dest)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dest)) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var rel = entry.FullName.Replace('/', '\\').TrimStart('\\');
            if (rel.Length == 0 || rel.EndsWith('\\')) continue; // directory entry

            var target = Path.GetFullPath(Path.Combine(root, rel));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in package: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    static async Task DownloadAsync(string url, string path, Action<long> onBytes, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long read = 0; int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            onBytes(read);
        }
    }

    static string Md5(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(fs));
    }

    /// <summary>Remove old versions and the download cache once a new version is installed.</summary>
    static void Cleanup(params string?[] keepVersions)
    {
        var keep = new HashSet<string>(keepVersions.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var d in Directory.GetDirectories(Paths.Versions))
        {
            var n = Path.GetFileName(d);
            // leave the installed player + Studio alone, and any install in progress
            if (!keep.Contains(n) && !n.EndsWith(".staging", StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(d, true); } catch { /* in use */ }
        }
        foreach (var d in Directory.GetDirectories(Paths.ModBackups))
            if (!keep.Contains(Path.GetFileName(d)))
                try { Directory.Delete(d, true); } catch { }
    }
}
