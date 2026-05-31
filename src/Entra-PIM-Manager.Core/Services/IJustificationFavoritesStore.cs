namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Per-role persistence for user-saved justification templates. Backed by a
/// JSON file in <c>%LocalAppData%</c>; one row per favourite, keyed by the
/// composite (tenant, kind, resourceId, scopeId) tuple that identifies a PIM
/// eligibility.
/// </summary>
/// <remarks>
/// Implementations must never log <see cref="JustificationFavorite.Text"/> —
/// justification content can carry sensitive incident references
/// (CLAUDE.md security conventions).
/// </remarks>
public interface IJustificationFavoritesStore
{
    /// <summary>
    /// Returns favourites for one eligibility, ordered by <see cref="JustificationFavorite.CreatedAt"/>
    /// (oldest first — so chip order stays stable as the user saves more).
    /// </summary>
    Task<IReadOnlyList<JustificationFavorite>> GetForRoleAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a new favourite for the given eligibility. Duplicates (exact
    /// text match for the same role) are ignored — the existing entry is
    /// returned so callers can refresh their UI consistently.
    /// </summary>
    Task<JustificationFavorite> AddAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        string text,
        CancellationToken ct = default);

    /// <summary>Removes a favourite by id. No-op if absent.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
