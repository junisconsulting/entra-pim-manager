namespace EntraPimManager.Core.Models;

/// <summary>
/// Outcome of one account's eligibility fetch in the cross-tenant fan-out.
/// Lets the UI tell "this tenant has nothing to activate" apart from "the
/// fetch failed" — the old empty-list-on-failure contract hid a tenant
/// without a PIM licence behind a bare "(0)".
/// </summary>
/// <param name="Items">The eligibilities; empty when the fetch failed.</param>
/// <param name="LoadError">User-facing reason the fetch failed, or <c>null</c> on success.</param>
public sealed record EligibilityFetchResult(
    IReadOnlyList<PimEligibility> Items,
    string? LoadError);
