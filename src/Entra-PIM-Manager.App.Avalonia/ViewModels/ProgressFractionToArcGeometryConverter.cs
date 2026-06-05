namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

/// <summary>
/// Converts a 0..1 remaining-time fraction into a <see cref="Geometry"/> that
/// traces a circular arc on a 44×44 viewport. The arc starts at the 12 o'clock
/// position and sweeps clockwise for <c>fraction * 360°</c>, so a row that was
/// just activated draws a near-full ring and an almost-expired row a tiny sliver.
/// Returns <c>null</c> for fractions ≤ ~0 so the path renders nothing.
/// </summary>
public sealed class ProgressFractionToArcGeometryConverter : IValueConverter
{
    /// <summary>Singleton instance for use as a static XAML resource.</summary>
    public static readonly ProgressFractionToArcGeometryConverter Instance = new();

    // 44×44 viewport. Radius sits 0.75px inside the 2.5px-thick track ring so the
    // coloured arc reads as nested just within it (matches the Ellipse below).
    private const double Radius = 20.0;
    private const double Center = 22.0;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double fraction)
        {
            return null;
        }

        fraction = Math.Clamp(fraction, 0.0, 1.0);
        if (fraction <= 0.001)
        {
            return null;
        }

        // ArcSegment can't render an exact 360° sweep; nudge below 1.0 so the
        // endpoint differs from the start by an imperceptible amount.
        if (fraction >= 1.0)
        {
            fraction = 0.9999;
        }

        var angle = fraction * 2.0 * Math.PI;
        var endX = Center + (Radius * Math.Sin(angle));
        var endY = Center - (Radius * Math.Cos(angle));
        var largeArc = fraction > 0.5 ? 1 : 0;

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"M {Center:F3},{Center - Radius:F3} A {Radius},{Radius} 0 {largeArc} 1 {endX:F3},{endY:F3}");
        return Geometry.Parse(path);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
