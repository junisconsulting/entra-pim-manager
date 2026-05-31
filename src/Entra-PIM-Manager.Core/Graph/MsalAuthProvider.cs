namespace EntraPimManager.Core.Graph;

using EntraPimManager.Core.Auth;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

/// <summary>
/// Kiota authentication provider that bridges the Graph SDK to <see cref="IAuthService"/>.
/// Every token request is pinned to the (identity, tenant, cloud) enrollment via
/// <see cref="IAuthService.AcquireTokenForAccountAsync"/>. A Conditional Access
/// claims challenge surfaced by Kiota is forwarded to MSAL.
/// </summary>
public sealed class MsalAuthProvider : IAuthenticationProvider
{
    private const string ClaimsKey = "claims";
    private readonly IAuthService _authService;
    private readonly string[] _scopes;
    private readonly string _accountId;
    private readonly string _tenantId;
    private readonly EntraCloud _cloud;

    public MsalAuthProvider(
        IAuthService authService,
        string[] scopes,
        string accountId,
        string tenantId,
        EntraCloud cloud)
    {
        _authService = authService;
        _scopes = scopes;
        _accountId = accountId;
        _tenantId = tenantId;
        _cloud = cloud;
    }

    /// <inheritdoc />
    public async Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? claims = null;
        if (additionalAuthenticationContext is not null
            && additionalAuthenticationContext.TryGetValue(ClaimsKey, out var value))
        {
            claims = value as string;
        }

        var result = await _authService
            .AcquireTokenForAccountAsync(_accountId, _tenantId, _cloud, _scopes, claims, cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Add("Authorization", $"Bearer {result.AccessToken}");
    }
}
