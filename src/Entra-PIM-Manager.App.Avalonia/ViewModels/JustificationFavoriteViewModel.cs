namespace EntraPimManager.AppAvalonia.ViewModels;

using EntraPimManager.Core.Models;

/// <summary>
/// List-row representation of a <see cref="JustificationFavorite"/>. Carries
/// a single-line label (newlines collapsed) for display, and the full text to
/// drop into the <see cref="ActivationPanelViewModel.Justification"/> field
/// when clicked. The row's TextBlock handles overflow via CharacterEllipsis,
/// so we no longer hard-cap the length here.
/// </summary>
public sealed class JustificationFavoriteViewModel
{
    public JustificationFavoriteViewModel(JustificationFavorite favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        Favorite = favorite;
        Label = CollapseWhitespace(favorite.Text);
    }

    /// <summary>The underlying model — needed by Delete/Apply commands.</summary>
    public JustificationFavorite Favorite { get; }

    /// <summary>Single-line label rendered on the row.</summary>
    public string Label { get; }

    /// <summary>Full text — bound to the row's tooltip.</summary>
    public string FullText => Favorite.Text;

    private static string CollapseWhitespace(string text)
        => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
