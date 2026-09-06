namespace EntraPimManager.Core.Caching;

using System.Collections.Concurrent;
using EntraPimManager.Core.Models;

/// <summary>
/// In-memory cache for parsed <see cref="ActivationPolicy"/> values with a short TTL.
/// Policies change infrequently; this avoids re-fetching on every dialog open.
/// Never persisted across restarts — PIM policy ids change on implicit onboarding.
/// </summary>
/// <remarks>
/// Cache keys are tenant-scoped (<c>{tenantId}:{kind}:{scopeId}:{resourceId}</c>) so
/// policies from different tenants cannot collide on the same role definition id.
/// The scope is part of the key because an Azure resource role carries a
/// different policy at every scope it is assigned at.
/// </remarks>
public sealed class PolicyCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public PolicyCache()
        : this(TimeProvider.System)
    {
    }

    public PolicyCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the cached policy for the given resource in the given tenant,
    /// or <c>null</c> if absent or expired.
    /// </summary>
    public ActivationPolicy? Get(string tenantId, PimResourceKind kind, string resourceId, string scopeId)
    {
        var key = BuildKey(tenantId, kind, resourceId, scopeId);
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return entry.Policy;
        }

        return null;
    }

    /// <summary>Stores <paramref name="policy"/> under the tenant-scoped key with the cache TTL.</summary>
    public void Set(string tenantId, PimResourceKind kind, string resourceId, string scopeId, ActivationPolicy policy)
    {
        var key = BuildKey(tenantId, kind, resourceId, scopeId);
        _entries[key] = new CacheEntry(policy, _timeProvider.GetUtcNow() + Ttl);
    }

    private static string BuildKey(string tenantId, PimResourceKind kind, string resourceId, string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return $"{tenantId}:{kind}:{scopeId}:{resourceId}";
    }

    private sealed record CacheEntry(ActivationPolicy Policy, DateTimeOffset ExpiresAt);
}
