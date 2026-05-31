namespace EntraPimManager.AppAvalonia.Converters;

using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Converts a person name string into up to two capital initials for the Hero
/// avatar circle. <c>"Ada Lovelace"</c> → <c>"AL"</c>; <c>"user@contoso.com"</c>
/// → <c>"US"</c>; <c>null</c>/empty → <c>"?"</c>.
/// </summary>
public sealed class InitialsConverter : IValueConverter
{
    /// <summary>Singleton for use as a static XAML resource.</summary>
    public static readonly InitialsConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var trimmed = text.Trim();

        // For email-like input, drop the domain so we don't pick "@".
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0)
        {
            trimmed = trimmed[..atIndex];
        }

        var parts = trimmed.Split(
            [' ', '.', '-', '_'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0][..2].ToUpperInvariant()
                : parts[0].ToUpperInvariant();
        }

        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
