namespace EntraPimManager.Core.Arm;

using System.Net.Http.Headers;
using EntraPimManager.Core.Auth;
using Microsoft.Identity.Client;

/// <summary>
/// HTTP handler that authorizes every Azure Resource Manager request with a
/// token from <see cref="IAuthService"/>, pinned to the (identity, tenant, cloud)
/// enrollment and the ARM scopes. The raw-<c>HttpClient</c> counterpart of
/// <see cref="Graph.MsalAuthProvider"/>.
/// </summary>
public sealed class ArmBearerTokenHandler : DelegatingHandler
{
    /// <summary>
    /// Per-request option carrying a decoded claims-challenge JSON. Set it to
    /// acquire the token for that request with a Conditional Access
    /// authentication context proactively — the PIM activation surface rejects
    /// an insufficient token with HTTP 400, not a 401 challenge. The counterpart
    /// of <see cref="Graph.AuthContextRequestOption"/>.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> ClaimsOption = new("EntraPimManager.Arm.Claims");

    private readonly IAuthService _authService;
    private readonly string[] _scopes;
    private readonly string _accountId;
    private readonly string _tenantId;
    private readonly EntraCloud _cloud;

    public ArmBearerTokenHandler(
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

        request.Options.TryGetValue(ClaimsOption, out var claims);

        // No WAM prompt for a background read. Every refresh touches this handler for
        // every enrollment, the interactive fallback runs under MSAL's process-wide
        // lock, and in a tenant that has not consented to the Azure permission the
        // prompt cannot succeed anyway — it would only stall the other accounts until
        // their timeout. A claims challenge is the exception: that one follows a click
        // on Activate, and the step-up prompt is the point.
        var silentOnly = claims is null;

        AuthenticationResult result;
        try
        {
            result = await _authService
                .AcquireTokenForAccountAsync(_accountId, _tenantId, _cloud, _scopes, claims, silentOnly, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // Distinguish "we gave up while signing in" from "Azure did not answer".
            // Both surface as a plain cancellation, and only the second one is the
            // network fault the generic timeout message would send an admin looking for.
            throw new ArmSignInPendingException(ex);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
