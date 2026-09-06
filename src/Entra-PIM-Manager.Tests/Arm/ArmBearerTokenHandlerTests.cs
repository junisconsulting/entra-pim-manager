namespace EntraPimManager.Tests.Arm;

using System.Net;
using EntraPimManager.Core.Arm;
using EntraPimManager.Core.Auth;
using EntraPimManager.Tests.TestSupport;
using Moq;

public sealed class ArmBearerTokenHandlerTests
{
    private const string AccountId = "oid-1";
    private const string TenantId = "tenant-1";
    private const EntraCloud Cloud = EntraCloud.Global;

    private static readonly string[] Scopes = EntraCloudInfo.ResourceManagerScopes(Cloud);

    [Fact]
    public async Task SendAsync_StampsBearerTokenAcquiredForTheArmScopes()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(AccountId, TenantId, Cloud, Scopes, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("arm-token"));
        var inner = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(authService.Object, inner);

        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/providers/Microsoft.Authorization/x"),
            CancellationToken.None);

        Assert.Equal("Bearer arm-token", inner.Requests[0].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendAsync_WithClaimsOption_ForwardsTheClaimsToMsal()
    {
        // The proactive auth-context path: the activation stamps the claims on
        // the request and the token for that one request must carry them.
        const string claims = """{"access_token":{"acrs":{"essential":true,"value":"c1"}}}""";
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(AccountId, TenantId, Cloud, Scopes, claims, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("stepped-up-token"));
        var inner = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
        using var invoker = CreateInvoker(authService.Object, inner);

        var request = new HttpRequestMessage(HttpMethod.Put, "https://management.azure.com/subscriptions/s/providers/x");
        request.Options.Set(ArmBearerTokenHandler.ClaimsOption, claims);
        await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal("Bearer stepped-up-token", inner.Requests[0].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendAsync_CancelledWhileAcquiringTheToken_ReportsAPendingSignIn()
    {
        // A cancelled token acquisition must not look like a network timeout: the
        // broker turns a missing tenant consent into a prompt nobody answers, and
        // the surface timeout then cancels us mid sign-in.
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(AccountId, TenantId, Cloud, Scopes, null, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var inner = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(authService.Object, inner);

        await Assert.ThrowsAsync<ArmSignInPendingException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/providers/Microsoft.Authorization/x"),
            CancellationToken.None));

        Assert.Empty(inner.Requests);
    }

    private static HttpMessageInvoker CreateInvoker(IAuthService authService, HttpMessageHandler inner)
    {
        var handler = new ArmBearerTokenHandler(authService, Scopes, AccountId, TenantId, Cloud)
        {
            InnerHandler = inner,
        };
        return new HttpMessageInvoker(handler);
    }
}
