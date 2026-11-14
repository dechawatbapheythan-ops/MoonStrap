using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MoonStrap;

public partial class BootstrapperWindow : Window
{
    public CancellationTokenSource Cancellation { get; } = new();
    bool _indeterminate;

    public BootstrapperWindow(string productName = "Roblox")
    {
        InitializeComponent();
        Header.Text = productName == "Roblox" ? "MoonStrap" : $"MoonStrap · {productName}";
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => { AddStars(); StartPulse(); SetIndeterminate(true); };
        Track.SizeChanged += (_, _) => { if (_indeterminate) SetIndeterminate(true); };
    }

    public void SetStatus(string text, double pct)
    {
        Status.Text = text;
        bool indeterminate = pct < 0;
        if (indeterminate != _indeterminate) SetIndeterminate(indeterminate);
        if (!indeterminate)
        {
            Fill.Width = Math.Clamp(pct, 0, 1) * Track.ActualWidth;
            Percent.Text = $"{pct:P0}";
        }
        else Percent.Text = "";
    }

    void SetIndeterminate(bool on)
    {
        _indeterminate = on;
        Shimmer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Fill.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on)
        {
            ShimmerX.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-Shimmer.Width, Math.Max(Track.ActualWidth, 1), TimeSpan.FromSeconds(1.3))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
        }
        else ShimmerX.BeginAnimation(TranslateTransform.XProperty, null);
    }

    void StartPulse()
    {
        var a = new DoubleAnimation(1, 1.06, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Pulse.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        Pulse.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void AddStars()
    {
        var rng = new Random(3);
        for (int i = 0; i < 40; i++)
        {
            double size = 1 + rng.NextDouble() * 1.4;
            var dot = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.1 + rng.NextDouble() * 0.5 };
            Canvas.SetLeft(dot, rng.NextDouble() * 430);
            Canvas.SetTop(dot, rng.NextDouble() * 200);
            if (rng.NextDouble() < 0.3)
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(dot.Opacity, 0.05, TimeSpan.FromSeconds(1 + rng.NextDouble() * 2))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            Stars.Children.Add(dot);
        }
    }

    /// <summary>Drops out of "always on top" so Roblox's window is in front, then fades and slides away.</summary>
    public Task FadeOutAndCloseAsync()
    {
        var done = new TaskCompletionSource();
        Topmost = false;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => { Close(); done.TrySetResult(); };
        BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Status.Text = "Cancelling...";
        Cancellation.Cancel();
    }
}
