namespace EntraPimManager.AppAvalonia.Services;

using Microsoft.Extensions.Logging;

/// <summary>
/// Reads <c>HKLM\SOFTWARE\Policies\junis\Entra-PIM-Manager\DisableJunisHosted</c>, which an
/// organization sets through Intune or Group Policy to remove the junis-hosted option.
/// A singleton, so the value is read and logged once, although the options step that
/// uses it runs twice at startup (docs/adr/0006).
/// </summary>
public sealed class JunisHostedPolicy
{
    public JunisHostedPolicy(ILogger<JunisHostedPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        IsDisabled = MachinePolicy.IsOn(MachinePolicy.DisableJunisHosted, logger);
        if (IsDisabled)
        {
            logger.LogInformation("junis-hosted turned off by machine policy");
        }
    }

    /// <summary>True when the organization turned off junis-hosted: the app then starts without the junis client id.</summary>
    public bool IsDisabled { get; }
}
