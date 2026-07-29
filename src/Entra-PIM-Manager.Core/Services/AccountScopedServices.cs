namespace EntraPimManager.Core.Services;

using System.Collections.Concurrent;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Graph;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IAccountScopedServices"/> implementation. Each bundle's
/// services share a single per-account <c>GraphServiceClient</c> from
/// <see cref="IGraphClientFactory.CreateFor"/>.
/// </summary>
public sealed class AccountScopedServices : IAccountScopedServices
{
    private readonly IGraphClientFactory _graphFactory;
    private readonly PolicyCache _policyCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, AccountScopedServiceBundle> _bundles =
        new(StringComparer.OrdinalIgnoreCase);

    public AccountScopedServices(IGraphClientFactory graphFactory, PolicyCache policyCache, ILoggerFactory loggerFactory)
    {
        _graphFactory = graphFactory;
        _policyCache = policyCache;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public AccountScopedServiceBundle GetServicesFor(SignedInAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.TenantId);

        // Mirror GraphClientFactory's composite key (identity, tenant, cloud)
        // so each enrollment gets its own service bundle — different clouds
        // hit different Graph endpoints.
        var key = $"{account.ObjectId}|{account.TenantId}|{account.Cloud}";
        return _bundles.GetOrAdd(key, _ => Build(account));
    }

    private AccountScopedServiceBundle Build(SignedInAccount account)
    {
        var graph = _graphFactory.CreateFor(account);
        var groupResolver = new GroupResolver(graph);
        var roleService = new PimRoleService(graph, _loggerFactory.CreateLogger<PimRoleService>());
        var groupService = new PimGroupService(graph, groupResolver, _loggerFactory.CreateLogger<PimGroupService>());
        var policyService = new PolicyService(graph, _policyCache);
        return new AccountScopedServiceBundle(roleService, groupService, policyService);
    }
}
