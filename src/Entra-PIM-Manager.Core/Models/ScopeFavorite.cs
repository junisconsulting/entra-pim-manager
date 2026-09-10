namespace EntraPimManager.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// A user-saved set of management groups and subscriptions to activate one Azure
/// eligibility on — "the two subscriptions this project lives in" — so a narrowed
/// activation is a click on a chip instead of a search through the estate.
/// </summary>
/// <remarks>
/// Keyed by (<see cref="ObjectId"/>, <see cref="TenantId"/>, <see cref="ResourceId"/>,
/// <see cref="ScopeId"/>) — the enrollment plus the eligibility. The kind is implied,
/// only Azure resource roles can narrow. Persisted by
/// <see cref="Services.IScopeFavoritesStore"/> in a file of its own, so an entry the app
/// cannot read costs the favourites and nothing else.
/// </remarks>
/// <param name="Name">
/// The user's own name for the set ("Project X"); <c>null</c> when they saved it
/// without one, in which case the scope names stand in.
/// </param>
/// <param name="ObjectId">
/// Object id of the account the favourite was saved under. <c>null</c> in entries
/// written before this field existed; those match on the tenant alone, as they did.
/// </param>
/// <param name="IsPinned">
/// Whether the set also sits under PINNED on the main page. Saving one turns this on —
/// saving is already the deliberate act — and the star on the row or on the chip turns
/// it off again, which leaves the favourite in the activation panel and only takes it
/// off the main page.
/// </param>
public sealed record ScopeFavorite(
    Guid Id,
    string TenantId,
    string ResourceId,
    string ScopeId,
    List<EligibleChildScope> Scopes,
    DateTimeOffset CreatedAt,
    string? Name = null,
    string? ObjectId = null,
    bool IsPinned = true)
{
    /// <summary>Chip text: the name, or the scope names when there is none.</summary>
    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? ScopesLabel : Name;

    /// <summary>The scope names, in saved order — the tooltip behind a named chip.</summary>
    [JsonIgnore]
    public string ScopesLabel => string.Join(", ", Scopes.Select(scope => scope.Name));

    /// <summary>
    /// True when this favourite belongs to the given eligibility under the given
    /// enrollment. Two accounts in one tenant can hold the same role at the same
    /// scope, and a favourite of one must not activate under the other.
    /// </summary>
    public bool BelongsTo(string objectId, string tenantId, string resourceId, string scopeId)
        => (ObjectId is null || string.Equals(ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            && string.Equals(TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ScopeId, scopeId, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the favourite is exactly this set of scopes, in any order.</summary>
    public bool HasSameScopes(IEnumerable<string> scopeIds)
        => new HashSet<string>(Scopes.Select(scope => scope.Id), StringComparer.OrdinalIgnoreCase)
            .SetEquals(scopeIds);
}
