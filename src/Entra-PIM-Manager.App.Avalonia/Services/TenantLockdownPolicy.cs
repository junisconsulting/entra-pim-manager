namespace EntraPimManager.AppAvalonia.Services;

using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Reads <c>HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager\TenantLockdown</c>, which an
/// organization sets through Intune or Group Policy. Read only: the app still writes
/// nothing outside the user's profile (docs/adr/0005).
/// </summary>
public sealed class TenantLockdownPolicy : ITenantLockdownPolicy
{
    // A public contract: admins deploy this path, so renaming it breaks their rollouts.
    private const string KeyPath = @"SOFTWARE\Policies\junis\Entra-PIM-Manager";
    private const string ValueName = "TenantLockdown";

    public TenantLockdownPolicy(ILogger<TenantLockdownPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        object? value;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            value = key?.GetValue(ValueName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the tenant lockdown policy; the tenant list stays editable");
            return;
        }

        if (TenantLockdownValue.IsMalformed(value))
        {
            logger.LogWarning("The tenant lockdown policy value is not a DWORD and is ignored; the tenant list stays editable");
            return;
        }

        IsActive = TenantLockdownValue.IsLocked(value);
        if (IsActive)
        {
            logger.LogInformation("Tenant lockdown policy active");
        }
    }

    /// <inheritdoc />
    public bool IsActive { get; }
}
