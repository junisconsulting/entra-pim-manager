namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

/// <summary>
/// Admins type this value by hand into an Intune script or a Group Policy preference.
/// Only a DWORD other than 0 locks; every other shape leaves the tenant list editable,
/// and a set-but-wrong type is reported so the admin finds the typo in the log.
/// </summary>
public sealed class TenantLockdownValueTests
{
    public static TheoryData<object?, bool, bool> Values => new()
    {
        { 1, true, false },
        { 2, true, false },
        { -1, true, false },
        { 0, false, false },
        { null, false, false },
        { "1", false, true },
        { new byte[] { 1, 0, 0, 0 }, false, true },
        { 1L, false, true },
        { new[] { "1" }, false, true },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void Interprets_TheRawRegistryValue(object? value, bool locked, bool malformed)
    {
        Assert.Equal(locked, TenantLockdownValue.IsLocked(value));
        Assert.Equal(malformed, TenantLockdownValue.IsMalformed(value));
    }
}
