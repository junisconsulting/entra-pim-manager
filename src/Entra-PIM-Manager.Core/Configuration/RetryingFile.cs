namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Opens one of the app's own JSON files for reading, waiting out the moment an AV scanner
/// holds it — typically at logon, when autostart and the scan of freshly touched files meet.
/// The stores never treat a failed read as an empty file, so without this a lock that lasts
/// a few milliseconds would leave them showing nothing until the next start.
/// </summary>
internal static class RetryingFile
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)];

    public static async Task<FileStream> OpenReadAsync(string path, CancellationToken ct)
    {
        foreach (var delay in Delays)
        {
            try
            {
                return File.OpenRead(path);
            }
            catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        return File.OpenRead(path);
    }
}
