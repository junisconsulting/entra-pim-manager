namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

/// <summary>
/// Admins type these values by hand into an Intune script or a Group Policy preference.
/// Only a DWORD other than 0 turns a policy on; every other shape leaves the app as it is
/// without it, and a set-but-wrong type is reported so the admin finds the typo in the log.
/// </summary>
public sealed class MachinePolicyValueTests
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
    public void Interprets_TheRawRegistryValue(object? value, bool on, bool malformed)
    {
        Assert.Equal(on, MachinePolicyValue.IsOn(value));
        Assert.Equal(malformed, MachinePolicyValue.IsMalformed(value));
    }
}
