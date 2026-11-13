namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// The green "Verified" check on a tenant card. It is only honest if one sign-in proves only
/// the registration it went through: every junis-hosted tenant shares one client id but
/// consents on its own, so a per-client-id record would turn all of them green at once.
/// </summary>
public sealed class UserSettingsVerifiedTests
{
    private const string JunisAppId = "5d0c1f2e-0000-4000-8000-0000000000f1";
    private const string OwnAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string TenantB = "7b1c4d2e-0000-4000-8000-0000000000bb";

    [Fact]
    public void JunisHosted_OneTenantVerified_DoesNotVerifyAnother()
    {
        var settings = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, JunisAppId, AppRegistrationHosting.JunisHosted);

        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantA.ToUpperInvariant(), JunisAppId, AppRegistrationHosting.JunisHosted));
        Assert.False(settings.IsRegistrationVerified(EntraCloud.Global, TenantB, JunisAppId, AppRegistrationHosting.JunisHosted));
        Assert.Null(settings.VerifiedClientIds);
    }

    [Fact]
    public void SelfHosted_KeepsTheExistingPerClientIdRecord()
    {
        // Unchanged from before junis-hosted: no migration, older versions read the same set.
        var settings = UserSettings.Default with { VerifiedClientIds = [OwnAppId] };

        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantA, OwnAppId, AppRegistrationHosting.SelfHosted));
        Assert.True(settings.IsRegistrationVerified(EntraCloud.Global, TenantB, OwnAppId, AppRegistrationHosting.SelfHosted));
        Assert.Null(settings.VerifiedJunisHostedRegistrations);
    }

    [Fact]
    public void ATenantSwitchedToJunisHosted_StartsUnverified()
    {
        // Its own registration worked, which proves nothing about junis' consent in that tenant.
        var settings = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, OwnAppId, AppRegistrationHosting.SelfHosted);

        Assert.False(settings.IsRegistrationVerified(EntraCloud.Global, TenantA, JunisAppId, AppRegistrationHosting.JunisHosted));
    }

    [Fact]
    public void WithRegistrationVerified_AlreadyVerified_ReturnsTheSameInstance()
    {
        // Same instance means the caller can skip rewriting the settings file.
        var once = UserSettings.Default.WithRegistrationVerified(EntraCloud.Global, TenantA, JunisAppId, AppRegistrationHosting.JunisHosted);

        Assert.Same(once, once.WithRegistrationVerified(EntraCloud.Global, TenantA, JunisAppId, AppRegistrationHosting.JunisHosted));
    }
}
