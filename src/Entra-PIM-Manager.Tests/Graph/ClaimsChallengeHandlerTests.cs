namespace EntraPimManager.Tests.Graph;

using System.Net;
using System.Net.Http.Headers;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Graph;
using EntraPimManager.Tests.TestSupport;
using Moq;

public sealed class ClaimsChallengeHandlerTests
{
    // base64url of {"x":1} — a non-empty payload so the parser returns a claims challenge.
    private const string ClaimsChallengeHeader = "claims=\"eyJ4IjoxfQ\"";
    private const string AccountId = "oid-1";
    private const string TenantId = "tenant-1";
    private const EntraCloud Cloud = EntraCloud.Global;

    private static readonly string[] Scopes = ["User.Read"];

    [Fact]
    public async Task SendAsync_SuccessResponse_PassesThroughWithoutReauthenticating()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var inner = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(authService.Object, inner);

        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task SendAsync_UnauthorizedWithoutClaims_ReturnsResponseUnchanged()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var inner = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var invoker = CreateInvoker(authService.Object, inner);

        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task SendAsync_UnauthorizedWithClaims_ReacquiresTokenAndRetriesOnce()
    {
        var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        unauthorized.Headers.WwwAuthenticate.Add(
            new AuthenticationHeaderValue("Bearer", ClaimsChallengeHeader));
        var inner = new FakeHttpMessageHandler(
            unauthorized,
            new HttpResponseMessage(HttpStatusCode.OK));

        var authService = new Mock<IAuthService>();
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                It.IsAny<string[]>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("stepped-up-token"));
        using var invoker = CreateInvoker(authService.Object, inner);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me")
        {
            Content = new StringContent("{}"),
        };
        var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.RequestCount);
        Assert.Equal("Bearer stepped-up-token", inner.Requests[1].Headers.Authorization?.ToString());
        authService.Verify(
            a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                It.IsAny<string[]>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static HttpMessageInvoker CreateInvoker(IAuthService authService, HttpMessageHandler inner)
    {
        var handler = new ClaimsChallengeHandler(authService, Scopes, AccountId, TenantId, Cloud)
        {
            InnerHandler = inner,
        };
        return new HttpMessageInvoker(handler);
    }
}
