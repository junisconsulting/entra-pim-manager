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
/// broker. A single multi-tenant <see cref="IPublicClientApplication"/> is created
/// lazily; all token operations are serialized through a semaphore. Enrolled
/// accounts are persisted via <see cref="IAccountStore"/> so the UI can render
/// the account list without unlocking the MSAL cache.
/// </summary>
/// <remarks>
/// Excluded from coverage: every path drives the live Windows WAM broker and the
/// DPAPI token cache, neither of which can be exercised by a unit test. Verified
/// by the manual WAM smoke test in <c>docs/manual-test-checklist.md</c>.
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

    // One PCA per sovereign cloud — MSAL recommends separate instances rather
    // than per-request authority overrides for cross-cloud scenarios. Each PCA
    // gets its own cache file via TokenCacheFactory so refresh tokens for
    // different clouds don't collide.
    private readonly Dictionary<EntraCloud, IPublicClientApplication> _pcas = [];

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
    public Task<SignedInAccount> AddAccountAsync(CancellationToken ct = default)
        => AddAccountCoreAsync(tenantIdOrDomain: null, EntraCloud.Global, ct);

    /// <inheritdoc />
    public Task<SignedInAccount> AddAccountForTenantAsync(
        string tenantIdOrDomain,
        EntraCloud cloud,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantIdOrDomain);
        return AddAccountCoreAsync(tenantIdOrDomain.Trim(), cloud, ct);
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
            await _accountStore.RemoveAsync(objectId, tenantId, ct).ConfigureAwait(false);

            // Only purge the MSAL cache entry when no other tenant enrollment in
            // the same cloud still uses the same home identity — otherwise we'd
            // kill the token bundle the remaining enrollment needs for silent
            // re-acquisition. Cross-cloud the cache is separate, so a remaining
            // enrollment in the other cloud doesn't count.
            var remaining = await _accountStore.GetAllAsync(ct).ConfigureAwait(false);
            var stillInUse = remaining.Any(a =>
                a.Cloud == cloud
                && string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
            if (!stillInUse)
            {
                var pca = await EnsurePcaAsync(cloud, ct).ConfigureAwait(false);
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
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(scopes);

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pca = await EnsurePcaAsync(cloud, ct).ConfigureAwait(false);
            var account = await FindMsalAccountAsync(pca, objectId).ConfigureAwait(false);

            if (account is null)
            {
                throw new MsalUiRequiredException(
                    MsalError.UserNullError,
                    $"No MSAL account for oid '{objectId}' in cloud '{cloud}'. The account must be re-enrolled.");
            }

            return await AcquireForAccountAsync(pca, account, tenantId, scopes, claimsChallenge, ct)
                .ConfigureAwait(false);
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

    private static string CacheFileFor(EntraCloud cloud) => cloud switch
    {
        EntraCloud.China => "msal-china.cache",
        _ => TokenCacheFactory.DefaultCacheFileName,
    };

    private async Task<SignedInAccount> AddAccountCoreAsync(
        string? tenantIdOrDomain,
        EntraCloud cloud,
        CancellationToken ct)
    {
        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pca = await EnsurePcaAsync(cloud, ct).ConfigureAwait(false);

            // Explicit sign-in always surfaces the WAM account picker (no
            // .WithAccount, no OperatingSystemAccount, Prompt.SelectAccount).
            // See memory: auth-no-sso-always-picker.
            var interactive = pca.AcquireTokenInteractive(_options.Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithParentActivityOrWindow(_windowTracker.GetCurrentWindowHandle());

            // When a tenant override is requested, MSAL drives the user to that
            // tenant's authority. The IAccount.HomeAccountId still reflects the
            // chosen identity's HOME tenant; only AuthenticationResult.TenantId
            // carries the target.
            if (tenantIdOrDomain is not null)
            {
                interactive = interactive.WithTenantId(tenantIdOrDomain);
            }

            var result = await interactive.ExecuteAsync(ct).ConfigureAwait(false);

            // The enrollment lives in the *target* tenant, not the home one —
            // that's what subsequent silent token requests must address.
            var tenantId = result.TenantId
                ?? result.Account.HomeAccountId?.TenantId
                ?? string.Empty;
            var objectId = result.Account.HomeAccountId?.ObjectId ?? string.Empty;

            EnforceTenantWhitelist(tenantId, result.Account, cloud);

            var account = new SignedInAccount(
                ObjectId: objectId,
                TenantId: tenantId,
                Username: result.Account.Username,
                DisplayName: result.ClaimsPrincipal?.FindFirst("name")?.Value,
                AddedAt: DateTimeOffset.UtcNow,
                Cloud: cloud);

            await _accountStore.UpsertAsync(account, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Account enrolled (oid {ObjectId}, tenant {TenantId}, cloud {Cloud}{TenantOverride})",
                account.ObjectId,
                account.TenantId,
                cloud,
                tenantIdOrDomain is null ? string.Empty : $", override {tenantIdOrDomain}");
            return account;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<AuthenticationResult> AcquireForAccountAsync(
        IPublicClientApplication pca,
        IAccount account,
        string tenantId,
        string[] scopes,
        string? claimsChallenge,
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
        catch (MsalUiRequiredException)
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

    private void EnforceTenantWhitelist(string tenantId, IAccount account, EntraCloud cloud)
    {
        if (_options.AllowedTenants is not { Length: > 0 } whitelist)
        {
            return;
        }

        if (whitelist.Any(t => string.Equals(t, tenantId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _logger.LogWarning(
            "Sign-in rejected: tenant {TenantId} (cloud {Cloud}) is not in the AllowedTenants whitelist (oid {ObjectId})",
            tenantId,
            cloud,
            account.HomeAccountId?.ObjectId);

        // Remove the just-cached account so it doesn't linger.
        if (_pcas.TryGetValue(cloud, out var pca))
        {
            _ = pca.RemoveAsync(account);
        }

        throw new MsalServiceException(
            "tenant_not_allowed",
            $"The signed-in tenant ({tenantId}) is not in the AllowedTenants whitelist.");
    }

    private async Task<IPublicClientApplication> EnsurePcaAsync(EntraCloud cloud, CancellationToken ct)
    {
        if (_pcas.TryGetValue(cloud, out var existing))
        {
            return existing;
        }

        ct.ThrowIfCancellationRequested();

        var pca = PublicClientApplicationBuilder
            .Create(_options.ClientId)

            // Multi-tenant authority within the chosen sovereign cloud: one PCA
            // per cloud, each serving any work-or-school tenant in that cloud.
            // The chosen tenant is encoded in each IAccount.HomeAccountId.
            .WithAuthority(EntraCloudInfo.MsalCloudInstance(cloud), AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{_options.ClientId}")
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
            .RegisterAsync(pca.UserTokenCache, CacheFileFor(cloud), ct)
            .ConfigureAwait(false);
        _pcas[cloud] = pca;
        return pca;
    }

    private void OnMsalLog(MsalLogLevel level, string message, bool containsPii)
    {
        if (containsPii)
        {
            return;
        }

        var mappedLevel = level switch
        {
            MsalLogLevel.Error => LogLevel.Error,
            MsalLogLevel.Warning => LogLevel.Warning,
            MsalLogLevel.Info => LogLevel.Information,
            MsalLogLevel.Verbose => LogLevel.Debug,
            _ => LogLevel.Debug,
        };

        _logger.Log(mappedLevel, "[MSAL] {Message}", message);
    }
}
