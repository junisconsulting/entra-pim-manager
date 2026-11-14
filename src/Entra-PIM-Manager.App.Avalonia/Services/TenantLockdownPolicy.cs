namespace EntraPimManager.AppAvalonia.Services;

using Microsoft.Extensions.Logging;

/// <summary>
/// Reads <c>HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager\TenantLockdown</c>, which an
/// organization sets through Intune or Group Policy. Read only: the app still writes
/// nothing outside the user's profile (docs/adr/0005).
/// </summary>
public sealed class TenantLockdownPolicy : ITenantLockdownPolicy
{
    public TenantLockdownPolicy(ILogger<TenantLockdownPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        IsActive = MachinePolicy.IsOn(MachinePolicy.TenantLockdown, logger);
        if (IsActive)
        {
            logger.LogInformation("Tenant lockdown policy active");
        }
    }

    /// <inheritdoc />
    public bool IsActive { get; }
}
