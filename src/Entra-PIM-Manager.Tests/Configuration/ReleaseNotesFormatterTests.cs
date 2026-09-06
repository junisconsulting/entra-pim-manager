namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

public sealed class ReleaseNotesFormatterTests
{
    [Fact]
    public void Parse_DropsTheTitleAndKeepsHeadingsBulletsAndParagraphs()
    {
        const string markdown = """
            # Entra PIM Manager 0.9.0

            **Add the delegated permission** before you roll out this update.

            ## New

            - **PIM for Azure Resources.** Azure RBAC roles appear in the list
              next to directory roles.
            - The network check probes `management.azure.com`.
            """;

        var notes = ReleaseNotesFormatter.Parse(markdown);

        Assert.Equal(4, notes.Count);
        Assert.Equal(ReleaseNoteStyle.Paragraph, notes[0].Style);
        Assert.Equal("Add the delegated permission before you roll out this update.", notes[0].Text);
        Assert.Equal(new ReleaseNote("New", ReleaseNoteStyle.Heading), notes[1]);

        // Only the lead sentence survives: the window is the summary, GitHub has the rest.
        Assert.Equal(ReleaseNoteStyle.Bullet, notes[2].Style);
        Assert.Equal("PIM for Azure Resources.", notes[2].Text);

        // No bold lead at all — the bullet is kept whole, and backticks are stripped
        // because there is no code style in that window.
        Assert.Equal("The network check probes management.azure.com.", notes[3].Text);
    }

    [Fact]
    public void Parse_BoldLeadWithoutSentenceEnd_KeepsTheWholeBullet()
    {
        // "The network check" on its own is not a headline, it is half of one. Truncating
        // a bullet whose bold span merely opens a phrase would say nothing at all.
        const string markdown = "- **The network check** probes the Azure host as its own row.";

        var note = Assert.Single(ReleaseNotesFormatter.Parse(markdown));

        Assert.Equal("The network check probes the Azure host as its own row.", note.Text);
    }

    [Theory]
    [InlineData(
        "- **A ticket system per tenant.** Set it once and every activation prefills it.",
        "A ticket system per tenant.")]
    [InlineData(
        "- **Search covers what the row shows:** role name, type and tenant.",
        "Search covers what the row shows:")]
    [InlineData(
        "- Activating at a narrower scope is not offered; the role uses its own.",
        "Activating at a narrower scope is not offered; the role uses its own.")]
    public void Parse_Bullet_KeepsTheLeadSentenceOrTheWholeLine(string markdown, string expected)
        => Assert.Equal(expected, Assert.Single(ReleaseNotesFormatter.Parse(markdown)).Text);

    [Fact]
    public void Parse_BoldInTheMiddleOfABullet_IsNotALead()
    {
        // The emphasis inside a sentence is not a headline; only an opening span is.
        const string markdown = "- The ticket system is **no longer mandatory** for activation.";

        var note = Assert.Single(ReleaseNotesFormatter.Parse(markdown));

        Assert.Equal("The ticket system is no longer mandatory for activation.", note.Text);
    }

    [Fact]
    public void Parse_Paragraph_IsNeverShortened()
    {
        // The required-action paragraph at the top is the one thing a user must read in
        // full — it is the reason the window exists on a release that needs consent.
        const string markdown = """
            **Add the delegated permission Azure Service Management.** Grant admin consent
            again, in every tenant, before you roll out this update.
            """;

        var note = Assert.Single(ReleaseNotesFormatter.Parse(markdown));

        Assert.Equal(ReleaseNoteStyle.Paragraph, note.Style);
        Assert.Equal(
            "Add the delegated permission Azure Service Management. Grant admin consent again, in every tenant, before you roll out this update.",
            note.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Parse_NothingToShow_ReturnsEmpty(string? markdown)
        => Assert.Empty(ReleaseNotesFormatter.Parse(markdown));
}
