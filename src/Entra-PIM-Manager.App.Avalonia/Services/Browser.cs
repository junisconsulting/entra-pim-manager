namespace EntraPimManager.AppAvalonia.Services;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hands a URL to the user's default browser. Shared so the app's links cannot drift apart in
/// how they fail — none is worth an error in the UI, so a failure is only logged.
/// </summary>
internal static class Browser
{
    public static void Open(string url, string target, ILogger logger)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open the {Target}", target);
        }
    }
}
