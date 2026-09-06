namespace EntraPimManager.Core.Auth;

using System.Diagnostics.CodeAnalysis;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using MsalLogLevel = Microsoft.Identity.Client.LogLevel;

/// <summary>
/// MSAL-based implementation of <see cref="IAuthService"/> using the Windows WAM
/// broker. One <see cref="IPublicClientApplication"/> per App Registration is created
/// lazily — the registration for an enrollment is derived from its (cloud, tenant) via
/// <see cref="EntraPimManagerOptions.ClientIdFor"/> on every call. All token
/// operations are serialized through a semaphore. Enrolled accounts are persisted via
/// <see cref="IAccountStore"/> so the UI can render the account list without
/// unlocking the MSAL cache.
/// </summary>
/// <remarks>
/// Excluded from coverage: every path drives the live Windows WAM broker and the
/// DPAPI token cache, neither of which can be exercised by a unit test. Verified
/// by the manual WAM smoke test in <c>.claude/manual-test-checklist.md</c>.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class MsalAuthService : IAuthService, IDisposable
{
    private readonly EntraPimManagerOptions _options;
    private readonly IWindowTracker _windowTracker;
    private readonly TokenCacheFactory _cacheFactory;
    private readonly IAccountStore _accountStore;
    private readonly ILogger<MsalAuthService> _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    // MSAL repeats the same environment warnings on every acquisition; this lets each
    // distinct one through once per process. See MsalLogThrottle for the numbers.
    private readonly MsalLogThrottle _msalLogThrottle = new();

    // One PCA per App Registration, keyed by client id. A PCA is bound to one
    // client id, and a client id exists in exactly one cloud, so the id is a
    // complete key. Each PCA gets its own cache file via TokenCacheFactory so
    // accounts and tokens of different registrations (and clouds) don't collide.
    private readonly Dictionary<string, IPublicClientApplication> _pcas = new(StringComparer.OrdinalIgnoreCase);

    // Separate broker-LESS PCAs for the device-code escape hatch. Device-code
    // flow is incompatible with WAM, so these omit .WithBroker and keep their
    // refresh tokens in their own cache files (the broker PCAs above store no RTs
    // — WAM owns those). Routing between the two sets is by SignedInAccount.AuthMethod.
    private readonly Dictionary<string, IPublicClientApplication> _deviceCodePcas = new(StringComparer.OrdinalIgnoreCase);

    public MsalAuthService(
        IOptions<EntraPimManagerOptions> options,
        IWindowTracker windowTracker,
        TokenCacheFactory cacheFactory,
        IAccountStore accountStore,
        ILogger<MsalAuthService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _windowTracker = windowTracker;
        _cacheFactory = cacheFactory;
        _accountStore = accountStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SignedInAccount> AddAccountAsync(
        string tenantId,
        EntraCloud cloud,
        CancellationToken ct = default)
        => AddAccountCoreAsync(RequireTenantGuid(tenantId), cloud, ct);

    /// <inheritdoc />
    public Task<SignedInAccount> AddAccountViaDeviceCodeAsync(
        string tenantId,
        EntraCloud cloud,
        Func<DeviceCodeChallenge, Task> onChallenge,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onChallenge);
        return AddAccountViaDeviceCodeCoreAsync(RequireTenantGuid(tenantId), cloud, onChallenge, ct);
    }

    /// <inheritdoc />
    public async Task RemoveAccountAsync(
        string objectId,
        string tenantId,
        EntraCloud cloud,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Capture how this enrollment authenticated BEFORE removing it, so we
            // clean up the matching cache (broker vs. device-code — separate caches).
            var removed = await _accountStore.GetByIdAsync(objectId, tenantId, ct).ConfigureAwait(false);
            var authMethod = removed?.AuthMethod ?? AuthMethod.Broker;

            await _accountStore.RemoveAsync(objectId, tenantId, ct).ConfigureAwait(false);

            // The MSAL account lives in the cache of the registration this
            // enrollment resolves to. If that registration is gone from the
            // configuration, there is no PCA to purge from — and refusing to
            // remove the enrollment would leave the user stuck with a dead entry.
            var clientId = _options.ClientIdFor(cloud, tenantId);
            if (clientId is null)
            {
                _logger.LogInformation(
                    "Account removed without cache purge — no App Registration for tenant {TenantId} in cloud {Cloud} (oid {ObjectId})",
                    tenantId,
                    cloud,
                    objectId);
                return;
            }

            // Only purge the MSAL cache entry when no other tenant enrollment
            // resolving to the SAME registration (same PCA, same cache file) with
            // the same auth method still uses the same home identity — otherwise
            // we'd kill the token bundle the remaining enrollment needs for silent
            // re-acquisition. Other registrations hold separate caches, so those
            // don't count.
            var remaining = await _accountStore.GetAllAsync(ct).ConfigureAwait(false);
            var stillInUse = remaining.Any(a =>
                a.AuthMethod == authMethod
                && string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_options.ClientIdFor(a.Cloud, a.TenantId), clientId, StringComparison.OrdinalIgnoreCase));
            if (!stillInUse)
            {
                var pca = authMethod == AuthMethod.DeviceCode
                    ? await EnsureDeviceCodePcaAsync(clientId, cloud, ct).ConfigureAwait(false)
                    : await EnsurePcaAsync(clientId, cloud, ct).ConfigureAwait(false);
                var msalAccount = await FindMsalAccountAsync(pca, objectId).ConfigureAwait(false);
                if (msalAccount is not null)
                {
                    await pca.RemoveAsync(msalAccount).ConfigureAwait(false);
                }
            }

            _logger.LogInformation(
                "Account removed (oid {ObjectId}, tenant {TenantId}, cloud {Cloud})",
                objectId,
                tenantId,
                cloud);
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SignedInAccount>> GetAllAccountsAsync(CancellationToken ct = default)
        => _accountStore.GetAllAsync(ct);

    /// <inheritdoc />
    public Task ReorderAccountsAsync(IReadOnlyList<SignedInAccount> orderedAccounts, CancellationToken ct = default)
        => _accountStore.ReorderAsync(orderedAccounts, ct);

    /// <inheritdoc />
    public async Task<AuthenticationResult> AcquireTokenForAccountAsync(
        string objectId,
        string tenantId,
        EntraCloud cloud,
        string[] scopes,
        string? claimsChallenge = null,
        bool silentOnly = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scopes);

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Route to the PCA matching how this account was enrolled. A
            // device-code account's refresh token lives in the broker-less PCA's
            // cache; renewing it via the broker PCA would miss the token and, on
            // interactive fallback, reintroduce the federated-SSO wrong-account
            // bug the device-code path exists to avoid.
            var enrollment = await _accountStore.GetByIdAsync(objectId, tenantId, ct).ConfigureAwait(false);
            var authMethod = enrollment?.AuthMethod ?? AuthMethod.Broker;

            var clientId = RequireClientId(cloud, tenantId);
            var pca = authMethod == AuthMethod.DeviceCode
                ? await EnsureDeviceCodePcaAsync(clientId, cloud, ct).ConfigureAwait(false)
                : await EnsurePcaAsync(clientId, cloud, ct).ConfigureAwait(false);
            var account = await FindMsalAccountAsync(pca, objectId).ConfigureAwait(false);

            if (account is null)
            {
                throw new MsalUiRequiredException(
                    MsalError.UserNullError,
                    $"No MSAL account for oid '{objectId}' in cloud '{cloud}' under client id '{clientId}'. The account must be re-enrolled.");
            }

            return authMethod == AuthMethod.DeviceCode
                ? await AcquireForDeviceCodeAccountAsync(pca, account, tenantId, scopes, ct).ConfigureAwait(false)
                : await AcquireForAccountAsync(pca, account, tenantId, scopes, claimsChallenge, silentOnly, ct).ConfigureAwait(false);
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _authLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<IAccount?> FindMsalAccountAsync(IPublicClientApplication pca, string objectId)
    {
        var accounts = await pca.GetAccountsAsync().ConfigureAwait(false);
        return accounts.FirstOrDefault(
            a => string.Equals(a.HomeAccountId?.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
    }

    // Every registration is pinned to a tenant, so a sign-in always names one.
    // Canonical GUID formatting keeps the persisted enrollment comparable with
    // the configuration entry regardless of how the user typed it.
    private static string RequireTenantGuid(string tenantId)
        => Guid.TryParse(tenantId, out var parsed)
            ? parsed.ToString()
            : throw new ArgumentException($"Tenant id '{tenantId}' is not a GUID.", nameof(tenantId));

    /// <summary>
    /// Client id pinned to <paramref name="tenantId"/> in <paramref name="cloud"/>, or a
    /// mapped error naming the missing registration and pointing at Settings.
    /// </summary>
    private string RequireClientId(EntraCloud cloud, string tenantId)
        => _options.ClientIdFor(cloud, tenantId)
            ?? throw new MsalServiceException(
                "app_registration_missing",
                $"No App Registration is configured for tenant {tenantId} in {EntraCloudInfo.DisplayName(cloud)}.");

    private async Task<SignedInAccount> AddAccountCoreAsync(
        string tenantId,
        EntraCloud cloud,
        CancellationToken ct)
    {
        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clientId = RequireClientId(cloud, tenantId);
            var pca = await EnsurePcaAsync(clientId, cloud, ct).ConfigureAwait(false);

            // Explicit sign-in always surfaces the WAM account picker (no
            // .WithAccount, no OperatingSystemAccount, Prompt.SelectAccount).
            // See memory: auth-no-sso-always-picker. The request goes to the
            // entry's tenant — what a single-tenant app requires and a multi-
            // tenant app accepts. The IAccount.HomeAccountId still reflects the
            // chosen identity's HOME tenant; AuthenticationResult.TenantId carries
            // the target.
            // One consent prompt covers Graph and Azure Resource Manager: the
            // ARM scope is consented here but never requested in the same
            // token — a token carries one audience. (The device-code builder
            // has no equivalent; those enrollments rely on tenant admin consent.)
            var result = await pca.AcquireTokenInteractive(_options.Scopes)
                .WithExtraScopesToConsent(EntraCloudInfo.ResourceManagerScopes(cloud))
                .WithPrompt(Prompt.SelectAccount)
                .WithTenantId(tenantId)
                .WithParentActivityOrWindow(_windowTracker.GetCurrentWindowHandle())
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            var account = ToEnrollment(result, tenantId, cloud, AuthMethod.Broker, pca);
            await _accountStore.UpsertAsync(account, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Account enrolled (oid {ObjectId}, tenant {TenantId}, cloud {Cloud}, client id {ClientId})",
                account.ObjectId,
                account.TenantId,
                cloud,
                clientId);
            return account;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<SignedInAccount> AddAccountViaDeviceCodeCoreAsync(
        string tenantId,
        EntraCloud cloud,
        Func<DeviceCodeChallenge, Task> onChallenge,
        CancellationToken ct)
    {
        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clientId = RequireClientId(cloud, tenantId);
            var pca = await EnsureDeviceCodePcaAsync(clientId, cloud, ct).ConfigureAwait(false);

            var result = await pca.AcquireTokenWithDeviceCode(
                    _options.Scopes,
                    deviceCodeResult =>
                    {
                        // MSAL invokes this once, before it starts polling. Hand the
                        // user-facing instructions to the UI. The user code is
                        // single-use and short-lived — never log it.
                        var challenge = new DeviceCodeChallenge(
                            UserCode: deviceCodeResult.UserCode,
                            VerificationUri: deviceCodeResult.VerificationUrl,
                            Message: deviceCodeResult.Message,
                            ExpiresOn: deviceCodeResult.ExpiresOn);
                        return onChallenge(challenge);
                    })
                .WithTenantId(tenantId)
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            var account = ToEnrollment(result, tenantId, cloud, AuthMethod.DeviceCode, pca);
            await _accountStore.UpsertAsync(account, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Account enrolled via device code (oid {ObjectId}, tenant {TenantId}, cloud {Cloud}, client id {ClientId})",
                account.ObjectId,
                account.TenantId,
                cloud,
                clientId);
            return account;
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// Builds the enrollment from a sign-in result after checking that Entra issued
    /// the token for the tenant that was asked for. With <c>.WithTenantId</c> on the
    /// request that is always the case; the guard exists so a surprise can never
    /// persist an enrollment whose tenant has no registration. The just-cached
    /// account is dropped then so it doesn't linger.
    /// </summary>
    private SignedInAccount ToEnrollment(
        AuthenticationResult result,
        string requestedTenantId,
        EntraCloud cloud,
        AuthMethod authMethod,
        IPublicClientApplication pca)
    {
        var issuedTenantId = result.TenantId ?? result.Account.HomeAccountId?.TenantId ?? string.Empty;
        if (!Guid.TryParse(issuedTenantId, out var issued) || issued != Guid.Parse(requestedTenantId))
        {
            _logger.LogWarning(
                "Sign-in rejected: requested tenant {RequestedTenantId} but the token was issued for {IssuedTenantId} (cloud {Cloud}, oid {ObjectId})",
                requestedTenantId,
                issuedTenantId,
                cloud,
                result.Account.HomeAccountId?.ObjectId);
            _ = pca.RemoveAsync(result.Account);
            throw new MsalServiceException(
                "tenant_mismatch",
                $"The sign-in landed in tenant {issuedTenantId} instead of {requestedTenantId}.");
        }

        return new SignedInAccount(
            ObjectId: result.Account.HomeAccountId?.ObjectId ?? string.Empty,
            TenantId: requestedTenantId,
            Username: result.Account.Username,
            DisplayName: result.ClaimsPrincipal?.FindFirst("name")?.Value,
            AddedAt: DateTimeOffset.UtcNow,
            Cloud: cloud,
            AuthMethod: authMethod);
    }

    private async Task<AuthenticationResult> AcquireForDeviceCodeAccountAsync(
        IPublicClientApplication pca,
        IAccount account,
        string tenantId,
        string[] scopes,
        CancellationToken ct)
    {
        // Device-code accounts renew silently from the broker-less PCA's own
        // refresh token. There is deliberately NO interactive fallback here: if
        // the RT is gone (MsalUiRequiredException), surface it so the caller can
        // prompt the user to re-run the explicit device-code enrollment. A silent
        // broker/interactive fallback would defeat the whole reason this account
        // uses device code. Claims challenges likewise require a fresh device-code
        // run, so they are not handled inline.
        var silent = pca.AcquireTokenSilent(scopes, account).WithTenantId(tenantId);
        var result = await silent.ExecuteAsync(ct).ConfigureAwait(false);
        _logger.LogDebug(
            "Device-code token acquired silently for tenant {TenantId} via {TokenSource}",
            tenantId,
            result.AuthenticationResultMetadata.TokenSource);
        return result;
    }

    private async Task<AuthenticationResult> AcquireForAccountAsync(
        IPublicClientApplication pca,
        IAccount account,
        string tenantId,
        string[] scopes,
        string? claimsChallenge,
        bool silentOnly,
        CancellationToken ct)
    {
        try
        {
            // .WithTenantId scopes the request to the specific tenant this
            // enrollment targets — necessary when the same home identity is
            // enrolled in multiple tenants and MSAL must pick the right token
            // bundle from its cache.
            var silent = pca.AcquireTokenSilent(scopes, account).WithTenantId(tenantId);
            if (claimsChallenge is not null)
            {
                silent = silent.WithClaims(claimsChallenge).WithForceRefresh(true);
            }

            var result = await silent.ExecuteAsync(ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Token acquired silently for tenant {TenantId} via {TokenSource}",
                tenantId,
                result.AuthenticationResultMetadata.TokenSource);
            return result;
        }
        catch (MsalUiRequiredException) when (!silentOnly)
        {
            // Re-auth required. Stay pinned to the same account and the same target tenant.
            var interactive = pca.AcquireTokenInteractive(scopes)
                .WithAccount(account)
                .WithTenantId(tenantId)
                .WithParentActivityOrWindow(_windowTracker.GetCurrentWindowHandle());
            if (claimsChallenge is not null)
            {
                interactive = interactive.WithClaims(claimsChallenge);
            }

            var result = await interactive.ExecuteAsync(ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Token acquired interactively for tenant {TenantId} via {TokenSource}",
                tenantId,
                result.AuthenticationResultMetadata.TokenSource);
            return result;
        }
    }

    private async Task<IPublicClientApplication> EnsurePcaAsync(string clientId, EntraCloud cloud, CancellationToken ct)
    {
        if (_pcas.TryGetValue(clientId, out var existing))
        {
            return existing;
        }

        ct.ThrowIfCancellationRequested();

        var pca = PublicClientApplicationBuilder
            .Create(clientId)

            // Work-or-school authority of the sovereign cloud. Every request
            // names its tenant via .WithTenantId, so a single-tenant registration
            // (which Entra refuses on /organizations, AADSTS50194) works exactly
            // like a multi-tenant one used in several tenants.
            .WithAuthority(EntraCloudInfo.MsalCloudInstance(cloud), AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{clientId}")
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
            {
                Title = "Entra-PIM-Manager",

                // Do NOT surface the Windows session account as a built-in candidate.
                // Entra PIM Manager is a privileged-access tool — the admin identity is
                // typically separate from the daily Windows login. See memory:
                // auth-no-sso-always-picker.
                ListOperatingSystemAccounts = false,
                MsaPassthrough = false,
            })
            .WithClientName("Entra-PIM-Manager")
            .WithLogging(OnMsalLog, MsalLogLevel.Info, enablePiiLogging: false)
            .Build();

        // Register the cache. MSAL holds the helper alive through the cache
        // callbacks, so we don't need to root it ourselves.
        await _cacheFactory
            .RegisterAsync(pca.UserTokenCache, $"msal-{clientId}.cache", ct)
            .ConfigureAwait(false);
        _pcas[clientId] = pca;
        return pca;
    }

    private async Task<IPublicClientApplication> EnsureDeviceCodePcaAsync(string clientId, EntraCloud cloud, CancellationToken ct)
    {
        if (_deviceCodePcas.TryGetValue(clientId, out var existing))
        {
            return existing;
        }

        ct.ThrowIfCancellationRequested();

        // Broker-LESS public client for the device-code escape hatch. No
        // .WithBroker (device code is incompatible with WAM) and no redirect URI
        // (device code doesn't use one). Unlike the broker PCA — where WAM owns
        // the refresh tokens — this PCA persists its own RTs, so it MUST have a
        // dedicated cache file that never collides with the broker caches.
        var pca = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(EntraCloudInfo.MsalCloudInstance(cloud), AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithClientName("Entra-PIM-Manager")
            .WithLogging(OnMsalLog, MsalLogLevel.Info, enablePiiLogging: false)
            .Build();

        await _cacheFactory
            .RegisterAsync(pca.UserTokenCache, $"msal-devicecode-{clientId}.cache", ct)
            .ConfigureAwait(false);
        _deviceCodePcas[clientId] = pca;
        return pca;
    }

    private void OnMsalLog(MsalLogLevel level, string message, bool containsPii)
    {
        if (containsPii)
        {
            return;
        }

        // MSAL's Info level is wire-protocol chatter — hundreds of megabytes
        // per day-file. From this app's perspective that is debug detail: it
        // only reaches the log when the user selects Debug in Settings.
        var mappedLevel = level switch
        {
            MsalLogLevel.Error => LogLevel.Error,
            MsalLogLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Debug,
        };

        // IsEnabled first: at the default level most of this is dropped anyway, and
        // there is no reason to normalise a message on its way to nowhere.
        if (!_logger.IsEnabled(mappedLevel) || !_msalLogThrottle.ShouldLog(mappedLevel, message))
        {
            return;
        }

        _logger.Log(mappedLevel, "[MSAL] {Message}", message);
    }
}
