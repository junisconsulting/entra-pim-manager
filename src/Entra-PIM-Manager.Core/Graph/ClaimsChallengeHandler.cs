namespace EntraPimManager.Core.Graph;

using System.Net;
using System.Net.Http.Headers;
using EntraPimManager.Core.Auth;

/// <summary>
/// HTTP handler that transparently satisfies a Conditional Access claims challenge:
/// on a <c>401</c> carrying a <c>claims</c> challenge it re-acquires a token via MSAL
/// (pinned to the configured account enrollment) and retries the original request
/// exactly once.
/// </summary>
public sealed class ClaimsChallengeHandler : DelegatingHandler
{
    private readonly IAuthService _authService;
    private readonly string[] _scopes;
    private readonly string _accountId;
    private readonly string _tenantId;
    private readonly EntraCloud _cloud;

    public ClaimsChallengeHandler(
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
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var claims = ClaimsChallengeParser.ExtractClaimsChallenge(response);
        if (claims is null)
        {
            // A 401 not caused by a claims challenge — let it bubble up.
            return response;
        }

        var result = await _authService
            .AcquireTokenForAccountAsync(_accountId, _tenantId, _cloud, _scopes, claims, cancellationToken)
            .ConfigureAwait(false);

        var retryRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        response.Dispose();
        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content is not null)
        {
            var buffer = await original.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            clone.Content = new ByteArrayContent(buffer);
            foreach (var contentHeader in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(contentHeader.Key, contentHeader.Value);
            }
        }

        return clone;
    }
}
