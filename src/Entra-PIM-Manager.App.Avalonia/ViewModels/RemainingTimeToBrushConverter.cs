namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

/// <summary>
/// Picks the ring / countdown text colour from the remaining time on an active
/// assignment. Three steps: accent (default), warning at ≤ 15 min, danger at
/// ≤ 5 min. Resolves to theme resources at convert time so palette changes
/// apply without rebuilding.
/// </summary>
public sealed class RemainingTimeToBrushConverter : IValueConverter
{
    /// <summary>Singleton instance for use as a static XAML resource.</summary>
    public static readonly RemainingTimeToBrushConverter Instance = new();

    private static readonly TimeSpan DangerThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan remaining)
        {
            return ResolveBrush("AccentTextBrush");
        }

        if (remaining <= TimeSpan.Zero)
        {
            return ResolveBrush("TextTertiaryBrush");
        }

        if (remaining <= DangerThreshold)
        {
            return ResolveBrush("DangerBrush");
        }

        if (remaining <= WarningThreshold)
        {
            return ResolveBrush("WarningBrush");
        }

        return ResolveBrush("AccentTextBrush");
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush? ResolveBrush(string key)
    {
        if (Application.Current is { } app
            && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }

        return null;
    }
}
