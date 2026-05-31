namespace EntraPimManager.Core.Models;

/// <summary>
/// A user-saved justification template, scoped to one eligibility. Replaces
/// re-typing the same incident reference / change-ticket boilerplate every
/// time the admin activates the same role.
/// </summary>
/// <remarks>
/// The composite key (<see cref="TenantId"/>, <see cref="Kind"/>,
/// <see cref="ResourceId"/>, <see cref="ScopeId"/>) mirrors the activation
/// matching key used throughout the app. <see cref="TenantId"/> is part of
/// the key because directory-role <c>ResourceId</c>s are constant across
/// tenants — without the tenant a "Global Admin" favourite would leak
/// between cross-tenant enrollments.
/// <para/>
/// <see cref="Text"/> can contain sensitive incident details and must never
/// be written to logs (see CLAUDE.md security conventions).
/// </remarks>
public sealed record JustificationFavorite(
    Guid Id,
    string TenantId,
    PimResourceKind Kind,
    string ResourceId,
    string ScopeId,
    string Text,
    DateTimeOffset CreatedAt);
