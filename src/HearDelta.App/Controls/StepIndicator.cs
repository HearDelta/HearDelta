using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearDelta.App.Controls;

/// <summary>Schrittanzeige „1 Einrichten · 2 Durchführen · 3 Ergebnis“.</summary>
public sealed class StepIndicator : StackPanel
{
    private static readonly string[] Labels = ["Einrichten", "Durchführen", "Ergebnis"];

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(StepIndicator),
        new PropertyMetadata(1, (target, _) => ((StepIndicator)target).Render()));

    public StepIndicator()
    {
        Orientation = Orientation.Horizontal;
        Loaded += (_, _) => Render();
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private void Render()
    {
        Children.Clear();
        var accent = FindBrush("AccentBrush", Colors.SlateBlue);
        var accentSoft = FindBrush("AccentSoftBrush", Colors.Lavender);
        var muted = FindBrush("MutedBrush", Colors.Gray);
        var border = FindBrush("BorderBrush", Colors.LightGray);
        for (var index = 0; index < Labels.Length; index++)
        {
            var number = index + 1;
            var isCurrent = number == Step;
            var isDone = number < Step;
            if (index > 0)
                Children.Add(new Border { Width = 36, Height = 1, Background = border, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });

            Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = isCurrent ? accent : isDone ? accentSoft : Brushes.White,
                BorderBrush = isCurrent || isDone ? accent : border,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = isDone ? "✓" : number.ToString(),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isCurrent ? Brushes.White : isDone ? accent : muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            Children.Add(new TextBlock
            {
                Text = Labels[index],
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 14,
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isCurrent ? accent : muted,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
    }

    private Brush FindBrush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
