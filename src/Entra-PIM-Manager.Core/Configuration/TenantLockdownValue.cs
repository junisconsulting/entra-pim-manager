namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Reads the organization's tenant-lockdown policy value (docs/adr/0005). Takes the raw
/// value as the registry hands it over: a boxed <see cref="int"/> for a DWORD, null when
/// the value is missing, a string or byte array when an admin set the wrong type.
/// </summary>
/// <remarks>
/// Anything but a DWORD leaves the tenant list editable: the lock is a convenience for
/// managed rollouts, not a security boundary, so a typo must not shut a user out.
/// </remarks>
public static class TenantLockdownValue
{
    /// <summary>True for a DWORD other than 0.</summary>
    public static bool IsLocked(object? value) => value is int dword && dword != 0;

    /// <summary>True when a value is set but is not a DWORD, so it is ignored and worth a warning.</summary>
    public static bool IsMalformed(object? value) => value is not null and not int;
}
