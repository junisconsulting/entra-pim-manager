namespace EntraPimManager.Tests.TestSupport;

/// <summary>
/// Loads JSON fixtures embedded under <c>Fixtures/</c> in the test assembly.
/// </summary>
public static class FixtureLoader
{
    /// <summary>Returns the text of the embedded fixture with the given file name.</summary>
    public static string Load(string fixtureName)
    {
        var assembly = typeof(FixtureLoader).Assembly;
        var resourceName = $"EntraPimManager.Tests.Fixtures.{fixtureName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded fixture not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
