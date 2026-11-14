namespace EntraPimManager.AppAvalonia.Services;

using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Reads the values an organization sets under
/// <c>HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager</c> through Intune or Group Policy.
/// Read only: the app still writes nothing outside the user's profile (docs/adr/0005,
/// docs/adr/0006).
/// </summary>
public static class MachinePolicy
{
    /// <summary>Locks the tenant list (docs/adr/0005).</summary>
    public const string TenantLockdown = "TenantLockdown";

    /// <summary>Removes the junis-hosted option (docs/adr/0006).</summary>
    public const string DisableJunisHosted = "DisableJunisHosted";

    // A public contract, like the value names: admins deploy this path, so renaming it breaks their rollouts.
    private const string KeyPath = @"SOFTWARE\Policies\junis\Entra-PIM-Manager";

    /// <summary>
    /// True when <paramref name="valueName"/> is a DWORD other than 0. Missing, unreadable or
    /// of another type, it counts as not set; the last two are logged as a warning.
    /// </summary>
    public static bool IsOn(string valueName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        object? value;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            value = key?.GetValue(valueName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the machine policy value {PolicyValue}; it is ignored, as if it were not set", valueName);
            return false;
        }

        if (MachinePolicyValue.IsMalformed(value))
        {
            logger.LogWarning("The machine policy value {PolicyValue} is not a DWORD and is ignored, as if it were not set", valueName);
            return false;
        }

        return MachinePolicyValue.IsOn(value);
    }
}
