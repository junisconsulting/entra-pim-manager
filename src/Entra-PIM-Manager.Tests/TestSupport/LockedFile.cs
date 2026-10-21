namespace EntraPimManager.Tests.TestSupport;

/// <summary>
/// Holds a file the way an AV scanner does for a moment: every other open fails with an
/// <see cref="IOException"/>, on Windows and — through .NET's advisory lock — on Linux too.
/// </summary>
public static class LockedFile
{
    public static FileStream Hold(string path)
        => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
}
