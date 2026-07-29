namespace EntraPimManager.Tests.Graph;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Graph;
using EntraPimManager.Tests.TestSupport;
using Microsoft.Kiota.Abstractions;
using Moq;

public sealed class MsalAuthProviderTests
{
    private const string AccountId = "oid-1";
    private const string TenantId = "tenant-1";
    private const EntraCloud Cloud = EntraCloud.Global;
    private static readonly string[] Scopes = ["User.Read"];

    [Fact]
    public async Task AuthenticateRequestAsync_AddsBearerAuthorizationHeader()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("access-token-1"));
        var provider = new MsalAuthProvider(authService.Object, Scopes, AccountId, TenantId, Cloud);
        var request = new RequestInformation
        {
            HttpMethod = Method.GET,
            URI = new Uri("https://graph.microsoft.com/v1.0/me"),
        };

        await provider.AuthenticateRequestAsync(request);

        Assert.Equal("Bearer access-token-1", Assert.Single(request.Headers["Authorization"]));
    }

    [Fact]
    public async Task AuthenticateRequestAsync_ForwardsClaimsChallengeToAuthService()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "claims-blob",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("access-token-2"));
        var provider = new MsalAuthProvider(authService.Object, Scopes, AccountId, TenantId, Cloud);
        var request = new RequestInformation
        {
            HttpMethod = Method.GET,
            URI = new Uri("https://graph.microsoft.com/v1.0/me"),
        };
        var context = new Dictionary<string, object> { ["claims"] = "claims-blob" };

        await provider.AuthenticateRequestAsync(request, context);

        authService.Verify(
            a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "claims-blob",
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("Bearer access-token-2", Assert.Single(request.Headers["Authorization"]));
    }

    [Fact]
    public async Task AuthenticateRequestAsync_AuthContextRequestOption_ForwardsClaimsToAuthService()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "proactive-claims",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("access-token-3"));
        var provider = new MsalAuthProvider(authService.Object, Scopes, AccountId, TenantId, Cloud);
        var request = new RequestInformation
        {
            HttpMethod = Method.POST,
            URI = new Uri("https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignmentScheduleRequests"),
        };
        request.AddRequestOptions([new AuthContextRequestOption { ClaimsJson = "proactive-claims" }]);

        await provider.AuthenticateRequestAsync(request);

        authService.Verify(
            a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "proactive-claims",
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("Bearer access-token-3", Assert.Single(request.Headers["Authorization"]));
    }

    [Fact]
    public async Task AuthenticateRequestAsync_ServerSentClaims_WinOverAuthContextRequestOption()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "server-claims",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestAuth.Result("access-token-4"));
        var provider = new MsalAuthProvider(authService.Object, Scopes, AccountId, TenantId, Cloud);
        var request = new RequestInformation
        {
            HttpMethod = Method.POST,
            URI = new Uri("https://graph.microsoft.com/v1.0/me"),
        };
        request.AddRequestOptions([new AuthContextRequestOption { ClaimsJson = "proactive-claims" }]);
        var context = new Dictionary<string, object> { ["claims"] = "server-claims" };

        await provider.AuthenticateRequestAsync(request, context);

        authService.Verify(
            a => a.AcquireTokenForAccountAsync(
                AccountId,
                TenantId,
                Cloud,
                Scopes,
                "server-claims",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
