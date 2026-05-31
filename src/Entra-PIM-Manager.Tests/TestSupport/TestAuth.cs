namespace EntraPimManager.Tests.TestSupport;

using Microsoft.Identity.Client;
using Moq;

/// <summary>
/// Builds MSAL <see cref="AuthenticationResult"/> instances for tests. Only
/// <see cref="AuthenticationResult.AccessToken"/> matters to the code under test;
/// the remaining constructor arguments are filler.
/// </summary>
public static class TestAuth
{
    /// <summary>Creates an <see cref="AuthenticationResult"/> carrying the given access token.</summary>
    public static AuthenticationResult Result(string accessToken) => new(
        accessToken: accessToken,
        isExtendedLifeTimeToken: false,
        uniqueId: "test-uid",
        expiresOn: DateTimeOffset.UtcNow.AddHours(1),
        extendedExpiresOn: DateTimeOffset.UtcNow.AddHours(1),
        tenantId: "test-tenant",
        account: Mock.Of<IAccount>(),
        idToken: "test-id-token",
        scopes: ["test-scope"],
        correlationId: Guid.NewGuid());
}
