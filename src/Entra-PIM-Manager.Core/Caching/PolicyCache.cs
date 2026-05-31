namespace EntraPimManager.Core.Caching;

using System.Collections.Concurrent;
using EntraPimManager.Core.Models;

/// <summary>
/// In-memory cache for parsed <see cref="ActivationPolicy"/> values with a short TTL.
/// Policies change infrequently; this avoids re-fetching on every dialog open.
/// Never persisted across restarts — PIM policy ids change on implicit onboarding.
/// </summary>
/// <remarks>
/// Cache keys are tenant-scoped (<c>{tenantId}:{kind}:{resourceId}</c>) so policies
/// from different tenants cannot collide on the same role definition id.
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
    public ActivationPolicy? Get(string tenantId, PimResourceKind kind, string resourceId)
    {
        var key = BuildKey(tenantId, kind, resourceId);
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return entry.Policy;
        }

        return null;
    }

    /// <summary>Stores <paramref name="policy"/> under the tenant-scoped key with the cache TTL.</summary>
    public void Set(string tenantId, PimResourceKind kind, string resourceId, ActivationPolicy policy)
    {
        var key = BuildKey(tenantId, kind, resourceId);
        _entries[key] = new CacheEntry(policy, _timeProvider.GetUtcNow() + Ttl);
    }

    private static string BuildKey(string tenantId, PimResourceKind kind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return $"{tenantId}:{kind}:{resourceId}";
    }

    private sealed record CacheEntry(ActivationPolicy Policy, DateTimeOffset ExpiresAt);
}
