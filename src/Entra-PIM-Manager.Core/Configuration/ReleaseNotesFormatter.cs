namespace EntraPimManager.Core.Configuration;

using System.Text;

/// <summary>
/// Turns a release-notes Markdown file into the handful of line kinds the
/// What's-new window renders.
/// </summary>
/// <remarks>
/// Deliberately not a Markdown parser: the notes are written by this project and
/// use exactly three constructs — <c>##</c> headings, <c>-</c> bullets and running
/// text — with <c>**bold**</c> and <c>`code`</c> inline. A real renderer would be a
/// dependency and a theming problem for one window. Wrapped lines are joined back
/// together, because the source files are hard-wrapped at 100 columns and would
/// otherwise break mid-sentence in a 380 px window.
/// <para/>
/// A bullet is reduced to its <b>lead sentence</b> — the opening <c>**…**</c> span, when
/// that span ends a sentence. Nobody reads a release in a tray popup; the window is the
/// scannable summary and the "releases on GitHub" button is the full text. This is the
/// window's parser alone: the GitHub release body, the Velopack welcome screen and the
/// in-app update prompt render the same file raw and stay complete.
/// <para/>
/// The convention that follows for anyone writing notes: open every bullet with a bold
/// sentence that stands on its own. A bold span that is only a phrase
/// (<c>- **The network check** probes …</c>) is deliberately <em>not</em> treated as a
/// lead — truncating there would leave a headline that says nothing — so the whole
/// bullet is kept instead.
/// </remarks>
public static class ReleaseNotesFormatter
{
    /// <summary>Parses <paramref name="markdown"/>; returns an empty list for empty input.</summary>
    public static IReadOnlyList<ReleaseNote> Parse(string? markdown)
    {
        var notes = new List<ReleaseNote>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return notes;
        }

        var pending = new StringBuilder();
        var pendingStyle = ReleaseNoteStyle.Paragraph;

        void Flush()
        {
            if (pending.Length > 0)
            {
                var text = pending.ToString();
                if (pendingStyle == ReleaseNoteStyle.Bullet)
                {
                    text = LeadSentence(text);
                }

                notes.Add(new ReleaseNote(Clean(text), pendingStyle));
                pending.Clear();
            }
        }

        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();

            // The H1 is the release title; the window's own header already says it.
            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                notes.Add(new ReleaseNote(Clean(line[3..]), ReleaseNoteStyle.Heading));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                pendingStyle = ReleaseNoteStyle.Bullet;
                pending.Append(line[2..]);
                continue;
            }

            // A continuation of the block above — the files wrap at 100 columns.
            if (pending.Length > 0)
            {
                pending.Append(' ').Append(line);
                continue;
            }

            pendingStyle = ReleaseNoteStyle.Paragraph;
            pending.Append(line);
        }

        Flush();
        return notes;
    }

    /// <summary>
    /// The opening bold span of <paramref name="bullet"/> when it ends a sentence,
    /// otherwise the bullet unchanged. Bold that merely opens a phrase, or sits in the
    /// middle of one, is left alone.
    /// </summary>
    private static string LeadSentence(string bullet)
    {
        if (!bullet.StartsWith("**", StringComparison.Ordinal))
        {
            return bullet;
        }

        var end = bullet.IndexOf("**", 2, StringComparison.Ordinal);
        if (end < 0)
        {
            return bullet;
        }

        var lead = bullet[2..end].TrimEnd();
        return lead.Length > 0 && lead[^1] is '.' or ':' or '!' or '?' ? lead : bullet;
    }

    private static string Clean(string text)
        => text.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();
}
