namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Persistence for the user's saved sets of Azure activation scopes, in a file of
/// their own. Mirrors <see cref="IJustificationFavoritesStore"/>, with one addition:
/// the shell renders the starred sets on the main page while it rebuilds its rows, so
/// the list is held in memory (<see cref="Current"/>) and every mutation announces
/// itself through <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// A file of its own rather than a field in <c>settings.json</c>: that file falls back
/// to defaults as a whole when it cannot be read, so one unreadable favourite would
/// also cost the user their theme, pins, aliases and tenant settings.
/// </remarks>
public interface IScopeFavoritesStore
{
    /// <summary>
    /// Raised after a favourite is added, removed or (un)starred — on the calling
    /// thread. Subscribers that touch UI must marshal to the dispatcher themselves.
    /// </summary>
    event Action? Changed;

    /// <summary>Every saved set. Empty until <see cref="LoadAsync"/> has run.</summary>
    IReadOnlyList<ScopeFavorite> Current { get; }

    /// <summary>
    /// The saved sets for one eligibility under one enrollment, oldest first — the one
    /// place that rule lives, so the panel's chips and the main page's rows cannot
    /// disagree about which favourite belongs where.
    /// </summary>
    IEnumerable<ScopeFavorite> ForRole(string objectId, string tenantId, string resourceId, string scopeId);

    /// <summary>
    /// Reads the file. Called once during host startup; a missing or corrupt file
    /// leaves <see cref="Current"/> empty and throws nothing.
    /// </summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Appends a set and persists it.</summary>
    Task AddAsync(ScopeFavorite favorite, CancellationToken ct = default);

    /// <summary>Removes a set by id. No-op if absent.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Stars or unstars a set — whether it also appears on the main page. No-op if absent.</summary>
    Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken ct = default);
}
