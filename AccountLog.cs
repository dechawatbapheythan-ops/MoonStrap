using System.Text.Json;

namespace MoonStrap;

/// <summary>One line in the account activity log.</summary>
/// <param name="Kind">"switch" = MoonStrap saw the active account change; "signin" = sign-in time reported by Roblox.</param>
public record AccountLogEntry(DateTime Time, long UserId, string Username, string DisplayName, string Kind);

/// <summary>
/// History of which Roblox account was signed in, and when. Stored in %LOCALAPPDATA%\MoonStrap\AccountLog.json.
/// </summary>
public static class AccountLog
{
    static readonly string FilePath = Path.Combine(Paths.Base, "AccountLog.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    const int MaxEntries = 300;
    static readonly TimeSpan SameEvent = TimeSpan.FromMinutes(10);

    public static List<AccountLogEntry> Load()
    {
        try { return JsonSerializer.Deserialize<List<AccountLogEntry>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    static void Save(List<AccountLogEntry> log)
    {
        var trimmed = log.OrderBy(e => e.Time).TakeLast(MaxEntries).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(trimmed, Opts));
    }

    public static void Clear() { try { File.Delete(FilePath); } catch { } }

    /// <summary>
    /// Merges Roblox's sign-in times into the log, then records a switch if the active account differs
    /// from the most recent entry. Returns the updated log, newest first.
    /// </summary>
    public static List<AccountLogEntry> Sync(RobloxAccount? active, IEnumerable<KnownAccount> known)
    {
        var log = Load();
        bool changed = false;

        foreach (var k in known)
        {
            if (k.LastSignIn is not { } t) continue;
            bool already = log.Any(e => e.UserId == k.UserId && (e.Time - t).Duration() < SameEvent);
            if (!already)
            {
                log.Add(new AccountLogEntry(t, k.UserId, k.Username, k.DisplayName, "signin"));
                changed = true;
            }
        }

        if (active != null)
        {
            var latest = log.OrderBy(e => e.Time).LastOrDefault();
            if (latest == null || latest.UserId != active.UserId)
            {
                log.Add(new AccountLogEntry(DateTime.Now, active.UserId, active.Username, active.DisplayName, "switch"));
                changed = true;
            }
        }

        if (changed) Save(log);
        return log.OrderByDescending(e => e.Time).ToList();
    }
}
