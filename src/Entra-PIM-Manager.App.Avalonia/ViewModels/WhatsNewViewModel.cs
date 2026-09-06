namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Configuration;

/// <summary>
/// Backing model for the What's-new window shown once after an update.
/// <see cref="Tray.WhatsNewController"/> fills it from the release-notes file
/// shipped with the build. UI text is English per the project conventions.
/// </summary>
public sealed partial class WhatsNewViewModel : ObservableObject
{
    /// <summary>Version headline, e.g. <c>"Version 0.9.0"</c>.</summary>
    [ObservableProperty]
    private string _versionText = string.Empty;

    /// <summary>The rendered notes, in file order.</summary>
    public ObservableCollection<ReleaseNoteItem> Notes { get; } = [];

    /// <summary>One line of the notes, flattened for the view's three styles.</summary>
    /// <param name="Text">Text to render.</param>
    /// <param name="IsHeading">Section heading.</param>
    /// <param name="IsBullet">List item — the view prefixes a bullet glyph.</param>
    public sealed record ReleaseNoteItem(string Text, bool IsHeading, bool IsBullet)
    {
        public static ReleaseNoteItem From(ReleaseNote note) => new(
            note.Text,
            note.Style == ReleaseNoteStyle.Heading,
            note.Style == ReleaseNoteStyle.Bullet);
    }
}
