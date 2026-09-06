namespace EntraPimManager.Core.Arm;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware;

/// <summary>
/// Builds per-account <see cref="HttpClient"/> instances for Azure Resource
/// Manager. The pipeline is Kiota's <see cref="RetryHandler"/> (already in the
/// dependency tree via the Graph SDK; retries <c>429</c>/<c>503</c> honouring
/// <c>Retry-After</c>) → <see cref="ArmBearerTokenHandler"/> →
/// <see cref="ClaimsChallengeHandler"/>. The bearer handler must sit above the
/// claims handler: the latter stamps a stepped-up token on its single retry, and
/// a bearer handler below it would overwrite that with the ordinary token.
/// </summary>
/// <remarks>
/// Excluded from coverage: pure construction wiring, mirroring <see cref="GraphClientFactory"/>.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class ArmClientFactory : IArmClientFactory
{
    private readonly IAuthService _authService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, HttpClient> _perAccountClients
        = new(StringComparer.OrdinalIgnoreCase);

    public ArmClientFactory(IAuthService authService, ILoggerFactory loggerFactory)
    {
        _authService = authService;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public HttpClient CreateFor(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.TenantId);

        // Same composite key as GraphClientFactory: the cloud decides the host
        // and the token audience.
        var key = $"{account.ObjectId}|{account.TenantId}|{account.Cloud}";
        return _perAccountClients.GetOrAdd(
            key,
            _ => BuildClient(account.ObjectId, account.TenantId, account.Cloud));
    }

    private HttpClient BuildClient(string accountId, string tenantId, EntraCloud cloud)
    {
        var scopes = EntraCloudInfo.ResourceManagerScopes(cloud);
        var claimsChallenge = new ClaimsChallengeHandler(
            _authService,
            scopes,
            accountId,
            tenantId,
            cloud,
            _loggerFactory.CreateLogger<ClaimsChallengeHandler>())
        {
            InnerHandler = new HttpClientHandler(),
        };
        var bearer = new ArmBearerTokenHandler(_authService, scopes, accountId, tenantId, cloud)
        {
            InnerHandler = claimsChallenge,
        };
        var retry = new RetryHandler
        {
            InnerHandler = bearer,
        };

        return new HttpClient(retry)
        {
            BaseAddress = new Uri(EntraCloudInfo.ResourceManagerBaseUrl(cloud) + "/"),
        };
    }
}
