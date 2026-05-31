namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;

/// <summary>
/// Factory for service bundles scoped to a single enrolled account. Each
/// bundle owns its own <c>GraphServiceClient</c> (and therefore its own auth
/// context, retry middleware, claims-challenge handler) plus per-account
/// instances of the PIM read/write and group-resolver services.
/// </summary>
/// <remarks>
/// Bundles are cached by <see cref="SignedInAccount.ObjectId"/> for the
/// lifetime of the factory so HTTP connection pooling and Kiota middleware
/// state are preserved across calls.
/// </remarks>
public interface IAccountScopedServices
{
    /// <summary>Returns the cached service bundle for <paramref name="account"/>, creating it on first use.</summary>
    AccountScopedServiceBundle GetServicesFor(SignedInAccount account);
}

/// <summary>
/// Holds the per-account service instances. The <see cref="PolicyService"/>
/// shares the singleton <see cref="Core.Caching.PolicyCache"/> — keys are
/// already tenant-scoped, so caching across accounts is safe.
/// </summary>
public sealed record AccountScopedServiceBundle(
    IPimRoleService RoleService,
    IPimGroupService GroupService,
    IPolicyService PolicyService);
