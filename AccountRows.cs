using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace MoonStrap;

/// <summary>Shared bits for rows that show an avatar which arrives after the row is drawn.</summary>
public abstract class AvatarRow : INotifyPropertyChanged
{
    public required long UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Username { get; init; }   // already prefixed with @

    Brush? _avatar;
    public Brush? AvatarBrush
    {
        get => _avatar;
        set { _avatar = value; PropertyChanged?.Invoke(this, new(nameof(AvatarBrush))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string When(DateTime t)
    {
        var day = t.Date;
        if (day == DateTime.Today) return "Today " + t.ToString("h:mm tt");
        if (day == DateTime.Today.AddDays(-1)) return "Yesterday " + t.ToString("h:mm tt");
        return t.ToString(t.Year == DateTime.Now.Year ? "MMM d, h:mm tt" : "MMM d yyyy, h:mm tt");
    }

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
        return t.ToString("MMM d, yyyy");
    }
}

/// <summary>A row in "Accounts on this PC".</summary>
public sealed class AccountRow : AvatarRow
{
    public required string Subtitle { get; init; }
    public required bool IsActive { get; init; }
    public string ProfileUrl => $"https://www.roblox.com/users/{UserId}/profile";
    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public Brush RowBackground => IsActive
        ? new SolidColorBrush(Color.FromArgb(0x1A, 0xA9, 0x9C, 0xFF))
        : Brushes.Transparent;
}

/// <summary>A row in the activity timeline.</summary>
public sealed class ActivityRow : AvatarRow
{
    public required string Verb { get; init; }
    public required string TimeText { get; init; }
    public required Brush DotBrush { get; init; }
}
