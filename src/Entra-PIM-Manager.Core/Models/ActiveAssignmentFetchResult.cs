namespace EntraPimManager.Core.Models;

/// <summary>
/// Outcome of one account's active-assignment fetch in the cross-tenant fan-out.
/// Mirrors <see cref="EligibilityFetchResult"/>, and for the same reason: a failed
/// fetch yields an empty list, which on its own reads exactly like "nothing is
/// active right now". Anything deciding that a role has been given up has to be
/// able to tell those apart.
/// </summary>
/// <param name="Items">The active assignments; empty when the fetch failed.</param>
/// <param name="LoadError">User-facing reason the fetch failed, or <c>null</c> on success.</param>
public sealed record ActiveAssignmentFetchResult(
    IReadOnlyList<ActiveAssignment> Items,
    string? LoadError);
