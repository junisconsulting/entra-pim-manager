namespace EntraPimManager.Core.Configuration;

/// <summary>
/// Interprets one of the organization's machine policy values (docs/adr/0005, docs/adr/0006).
/// Takes the raw value as the registry hands it over: a boxed <see cref="int"/> for a DWORD,
/// null when the value is missing, a string or byte array when an admin set the wrong type.
/// </summary>
/// <remarks>
/// Anything but a DWORD leaves the app as it is without the policy: the policies are a
/// convenience for managed rollouts, not a security boundary, so a typo must not shut a user out.
/// </remarks>
public static class MachinePolicyValue
{
    /// <summary>True for a DWORD other than 0.</summary>
    public static bool IsOn(object? value) => value is int dword && dword != 0;

    /// <summary>True when a value is set but is not a DWORD, so it is ignored and worth a warning.</summary>
    public static bool IsMalformed(object? value) => value is not null and not int;
}
