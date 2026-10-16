namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Whose App Registration a tenant signs in with. Derived from the tenant's client id on
/// every call (<see cref="EntraPimManagerOptions.HostingFor"/>), never persisted — the
/// entry stores the client id and nothing else.
/// </summary>
public enum AppRegistrationHosting
{
    /// <summary>The tenant's own App Registration, created by its admin.</summary>
    SelfHosted,

    /// <summary>
    /// The multi-tenant registration junis publishes; the tenant's admin only consents
    /// to it. Global cloud only, and without public client flows, so no device code.
    /// </summary>
    JunisHosted,
}
