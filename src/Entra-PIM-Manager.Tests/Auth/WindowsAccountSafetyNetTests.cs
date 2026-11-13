namespace EntraPimManager.Tests.Auth;

using EntraPimManager.Core.Auth;
using Microsoft.Identity.Client;

/// <summary>
/// The rules that let a lost sign-in be recovered through the Windows account. A slip here
/// either signs the wrong identity in to the app, or turns a host that loses its token cache
/// at every logon (#13) into a stream of failed sign-ins in the tenant's sign-in log.
/// </summary>
public sealed class WindowsAccountSafetyNetTests
{
    // Microsoft's published pair for a Microsoft Entra group (Graph group-get sample);
    // users are mapped the same way.
    private const string Sid = "S-1-12-1-45981654-1196986259-3072312199-363020343";
    private const string ObjectId = "02bd9fd6-8f93-4758-87c3-1fb73740a315";
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string OtherTenantId = "66666666-7777-8888-9999-000000000000";
    private static readonly string[] Graph = new[] { "https://graph.microsoft.com/.default" };
    private static readonly string[] Azure = new[] { "https://management.azure.com/user_impersonation" };

    [Fact]
    public void ObjectIdFromSid_EntraSid_GivesTheObjectId()
    {
        Assert.Equal(Guid.Parse(ObjectId), WindowsAccountSafetyNet.ObjectIdFromSid(Sid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-1013")]
    [InlineData("S-1-12-1-45981654-1196986259-3072312199")]
    [InlineData("S-1-12-1-45981654-1196986259-3072312199-363020343-1")]
    [InlineData("S-1-12-1-45981654-1196986259-3072312199-4294967296")]
    [InlineData("S-1-12-2-45981654-1196986259-3072312199-363020343")]
    public void ObjectIdFromSid_AnyOtherSid_GivesNothing(string? sid)
    {
        Assert.Null(WindowsAccountSafetyNet.ObjectIdFromSid(sid));
    }

    [Theory]
    [InlineData(MsalError.UserNullError)]
    [InlineData("wam_no_account_for_id")]
    public void ShouldTry_LostSignInOfTheWindowsUser_Tries(string errorCode)
    {
        var net = new WindowsAccountSafetyNet(Sid);

        Assert.True(net.ShouldTry(errorCode, AuthMethod.Broker, null, ObjectId.ToUpperInvariant(), TenantId, Graph));
    }

    [Fact]
    public void ShouldTry_AnyOtherError_DoesNotTry()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        Assert.False(net.ShouldTry(MsalError.InvalidGrantError, AuthMethod.Broker, null, ObjectId, TenantId, Graph));
    }

    [Fact]
    public void ShouldTry_DeviceCodeAccount_DoesNotTry()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.DeviceCode, null, ObjectId, TenantId, Graph));
    }

    [Fact]
    public void ShouldTry_AccountNoLongerEnrolled_DoesNotTry()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        Assert.False(net.ShouldTry(MsalError.UserNullError, null, null, ObjectId, TenantId, Graph));
    }

    [Fact]
    public void ShouldTry_WhileAnsweringAClaimsChallenge_DoesNotTry()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, "{\"access_token\":{}}", ObjectId, TenantId, Graph));
    }

    [Theory]
    [InlineData("S-1-12-1-45981654-1196986259-3072312199-363020343", "aaaaaaaa-0000-0000-0000-000000000000")]
    [InlineData("S-1-12-1-45981654-1196986259-3072312199-363020343", "not-a-guid")]
    [InlineData(null, ObjectId)]
    public void ShouldTry_AccountIsNotProvablyTheWindowsUser_DoesNotTry(string? sessionSid, string objectId)
    {
        var net = new WindowsAccountSafetyNet(sessionSid);

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, objectId, TenantId, Graph));
    }

    [Fact]
    public void RecordFailure_StopsRetriesForThatEnrollmentOnly()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        net.RecordFailure(ObjectId, TenantId, Graph, new MsalUiRequiredException("failed_to_acquire_token_silently_from_broker", "no"));

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, TenantId, Graph));
        Assert.True(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, OtherTenantId, Graph));
    }

    [Fact]
    public void RecordFailure_ForAzure_LeavesTheAttemptForGraph()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        net.RecordFailure(ObjectId, TenantId, Azure, new MsalUiRequiredException("failed_to_acquire_token_silently_from_broker", "no consent"));

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, TenantId, Azure));
        Assert.True(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, TenantId, Graph));
    }

    [Fact]
    public void RecordFailure_AnotherAccountReturned_CountsAsAFailure()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        net.RecordFailure(ObjectId, TenantId, Graph);

        Assert.False(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, TenantId, Graph));
    }

    [Fact]
    public void RecordFailure_NetworkError_DoesNotCount()
    {
        var net = new WindowsAccountSafetyNet(Sid);

        net.RecordFailure(ObjectId, TenantId, Graph, new MsalServiceException("no_network", "offline") { IsRetryable = true });

        Assert.True(net.ShouldTry(MsalError.UserNullError, AuthMethod.Broker, null, ObjectId, TenantId, Graph));
    }

    [Theory]
    [InlineData(ObjectId, TenantId, true)]
    [InlineData("02BD9FD6-8F93-4758-87C3-1FB73740A315", TenantId, true)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000000", TenantId, false)]
    [InlineData(ObjectId, OtherTenantId, false)]
    [InlineData(null, TenantId, false)]
    [InlineData(ObjectId, null, false)]
    public void IsSameAccount_ChecksObjectIdAndTenant(string? resultObjectId, string? resultTenantId, bool expected)
    {
        Assert.Equal(expected, WindowsAccountSafetyNet.IsSameAccount(resultObjectId, resultTenantId, ObjectId, TenantId));
    }
}
