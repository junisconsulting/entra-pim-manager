namespace EntraPimManager.Core.Graph;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

/// <summary>
/// Builds <see cref="GraphServiceClient"/> instances. The handler chain is the
/// Graph SDK's default middleware (which already retries <c>429</c>/<c>503</c>
/// honouring <c>Retry-After</c>) plus a <see cref="ClaimsChallengeHandler"/> for
/// Conditional Access. Per-account clients are cached so connection pooling and
/// Kiota middleware state are preserved across calls.
/// </summary>
/// <remarks>
/// Excluded from coverage: pure construction wiring for the Graph SDK middleware
/// pipeline, with no branching logic to exercise.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IAuthService _authService;
    private readonly string[] _scopes;
    private readonly ConcurrentDictionary<string, GraphServiceClient> _perAccountClients
        = new(StringComparer.OrdinalIgnoreCase);

    public GraphClientFactory(IAuthService authService, IOptions<EntraPimManagerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _authService = authService;
        _scopes = options.Value.Scopes;
    }

    /// <inheritdoc />
    public GraphServiceClient CreateFor(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.TenantId);

        // Composite key includes cloud so the same home identity enrolled in
        // a Global and a China tenant produces two distinct Graph clients with
        // different base URLs and different token-request paths.
        var key = $"{account.ObjectId}|{account.TenantId}|{account.Cloud}";
        return _perAccountClients.GetOrAdd(
            key,
            _ => BuildClient(account.ObjectId, account.TenantId, account.Cloud));
    }

    private GraphServiceClient BuildClient(string accountId, string tenantId, EntraCloud cloud)
    {
        var handlers = Microsoft.Graph.GraphClientFactory.CreateDefaultHandlers();
        handlers.Add(new ClaimsChallengeHandler(_authService, _scopes, accountId, tenantId, cloud));

        var httpClient = Microsoft.Graph.GraphClientFactory.Create(handlers);
        return new GraphServiceClient(
            httpClient,
            new MsalAuthProvider(_authService, _scopes, accountId, tenantId, cloud),
            EntraCloudInfo.GraphBaseUrl(cloud));
    }
}
