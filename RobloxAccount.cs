using System.Net.Http;
using System.Text.Json;

namespace MoonStrap;

public record RobloxAccount(long UserId, string Username, string DisplayName)
{
    public string ProfileUrl => $"https://www.roblox.com/users/{UserId}/profile";
}

/// <summary>An account Roblox remembers on this PC (from its account switcher list).</summary>
public record KnownAccount(long UserId, string Username, string DisplayName, DateTime? LastSignIn, string? LoginMethod);

/// <summary>
/// Finds the Roblox account signed in on this PC and its avatar headshot.
/// Only the public UserId / Username / DisplayName fields are read from Roblox's local app storage;
/// login tokens in that file are never touched. Names and the headshot come from Roblox's
/// public, no-login APIs.
/// </summary>
public static class AccountService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static string AppStorage => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "LocalStorage", "appStorage.json");

    public static string StorageFolder => Path.GetDirectoryName(AppStorage)!;
    public static string StorageFileName => Path.GetFileName(AppStorage);

    static JsonDocument OpenStorage()
    {
        // FileShare.ReadWrite: Roblox may have the file open
        using var fs = new FileStream(AppStorage, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonDocument.Parse(fs);
    }

    /// <summary>
    /// Every account in Roblox's account switcher list. Only id / names / sign-in time / method are read.
    /// </summary>
    public static List<KnownAccount> ReadKnownAccounts()
    {
        var list = new List<KnownAccount>();
        try
        {
            using var doc = OpenStorage();
            if (!doc.RootElement.TryGetProperty("PreviousAccountsList", out var raw) || raw.ValueKind != JsonValueKind.String)
                return list;
            using var inner = JsonDocument.Parse(raw.GetString()!);
            foreach (var prop in inner.RootElement.EnumerateObject())
            {
                var e = prop.Value;
                string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (!long.TryParse(S("userId") is { Length: > 0 } u ? u : prop.Name, out var id) || id <= 0) continue;

                DateTime? signIn = null;
                if (e.TryGetProperty("signInTimestamp", out var ts) && ts.TryGetInt64(out var secs) && secs > 0)
                    signIn = DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime;

                var user = S("username");
                var display = S("displayName");
                list.Add(new KnownAccount(id, user, display.Length > 0 ? display : user, signIn, S("loginMethod") is { Length: > 0 } m ? m : null));
            }
        }
        catch { }
        return list.OrderByDescending(a => a.LastSignIn ?? DateTime.MinValue).ToList();
    }

    public static RobloxAccount? ReadLocal()
    {
        try
        {
            using var doc = OpenStorage();
            var root = doc.RootElement;

            long id = 0;
            if (root.TryGetProperty("UserId", out var idEl))
            {
                if (idEl.ValueKind == JsonValueKind.Number) idEl.TryGetInt64(out id);
                else if (idEl.ValueKind == JsonValueKind.String) long.TryParse(idEl.GetString(), out id);
            }
            if (id <= 0) return null;

            string Str(string name) => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
            var user = Str("Username");
            var display = Str("DisplayName");
            return new RobloxAccount(id, user, display.Length > 0 ? display : user);
        }
        catch { return null; }
    }

    /// <summary>Current names from Roblox (in case they changed since the last sign-in).</summary>
    public static async Task<RobloxAccount> RefreshAsync(RobloxAccount acc)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://users.roblox.com/v1/users/{acc.UserId}"));
            var r = doc.RootElement;
            return acc with
            {
                Username = r.GetProperty("name").GetString() ?? acc.Username,
                DisplayName = r.GetProperty("displayName").GetString() ?? acc.DisplayName,
            };
        }
        catch { return acc; }
    }

    static string CachePath(long id) => Path.Combine(Paths.Base, $"avatar-{id}.png");

    /// <summary>Cached headshot if we have one (instant, works offline).</summary>
    public static string? CachedHeadshot(long id) => File.Exists(CachePath(id)) ? CachePath(id) : null;

    /// <summary>Downloads a fresh 150x150 headshot and caches it. Returns the local file, or null.</summary>
    public static async Task<string?> DownloadHeadshotAsync(long id)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={id}&size=150x150&format=Png&isCircular=false"));
            var item = doc.RootElement.GetProperty("data")[0];
            if (item.GetProperty("state").GetString() != "Completed") return CachedHeadshot(id);
            var url = item.GetProperty("imageUrl").GetString();
            if (string.IsNullOrEmpty(url)) return CachedHeadshot(id);

            var bytes = await Http.GetByteArrayAsync(url);
            var path = CachePath(id);
            await File.WriteAllBytesAsync(path + ".tmp", bytes);
            File.Move(path + ".tmp", path, overwrite: true);
            return path;
        }
        catch { return CachedHeadshot(id); }
    }

    /// <summary>Headshots for many accounts in one API call (cached ones are reused unless older than a day).</summary>
    public static async Task<Dictionary<long, string>> DownloadHeadshotsAsync(IEnumerable<long> ids)
    {
        var result = new Dictionary<long, string>();
        var need = new List<long>();
        foreach (var id in ids.Distinct())
        {
            var c = CachedHeadshot(id);
            if (c != null) result[id] = c;
            if (c == null || File.GetLastWriteTime(c) < DateTime.Now.AddDays(-1)) need.Add(id);
        }
        if (need.Count == 0) return result;

        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={string.Join(",", need)}&size=150x150&format=Png&isCircular=false"));
            var downloads = doc.RootElement.GetProperty("data").EnumerateArray()
                .Where(d => d.GetProperty("state").GetString() == "Completed")
                .Select(d => (id: d.GetProperty("targetId").GetInt64(), url: d.GetProperty("imageUrl").GetString()))
                .Where(x => !string.IsNullOrEmpty(x.url))
                .Select(async x =>
                {
                    try
                    {
                        var bytes = await Http.GetByteArrayAsync(x.url);
                        var path = CachePath(x.id);
                        await File.WriteAllBytesAsync(path + ".tmp", bytes);
                        File.Move(path + ".tmp", path, overwrite: true);
                        lock (result) result[x.id] = path;
                    }
                    catch { }
                });
            await Task.WhenAll(downloads);
        }
        catch { }
        return result;
    }
}
