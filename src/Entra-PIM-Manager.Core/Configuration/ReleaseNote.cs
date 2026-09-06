namespace EntraPimManager.Core.Configuration;

/// <summary>How one line of the release notes is rendered in the What's-new window.</summary>
public enum ReleaseNoteStyle
{
    /// <summary>A section heading ("New", "Notes").</summary>
    Heading,

    /// <summary>One list item.</summary>
    Bullet,

    /// <summary>Running text — in our notes, the required-action paragraph at the top.</summary>
    Paragraph,
}

/// <summary>One rendered line of the release notes.</summary>
/// <param name="Text">The text, with the Markdown emphasis markers already removed.</param>
/// <param name="Style">How to render it.</param>
public sealed record ReleaseNote(string Text, ReleaseNoteStyle Style);
