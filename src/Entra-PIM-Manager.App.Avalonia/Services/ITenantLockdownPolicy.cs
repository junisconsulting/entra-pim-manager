namespace EntraPimManager.AppAvalonia.Services;

/// <summary>
/// The organization's lock on the tenant list, read once at startup from a machine policy
/// the app never writes (docs/adr/0005).
/// </summary>
public interface ITenantLockdownPolicy
{
    /// <summary>True when the organization manages the tenant list: no adding, re-hosting or removing.</summary>
    bool IsActive { get; }
}
