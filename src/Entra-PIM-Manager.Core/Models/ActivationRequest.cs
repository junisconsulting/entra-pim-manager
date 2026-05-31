namespace EntraPimManager.Core.Models;

/// <summary>
/// A user's request to activate a single eligibility. The service computes the
/// start time itself (now) — v1 does not support future scheduling.
/// </summary>
/// <param name="Eligibility">The eligibility being activated.</param>
/// <param name="Duration">Requested activation duration; must not exceed the policy maximum.</param>
/// <param name="Justification">Justification text, if the policy requires one.</param>
/// <param name="Ticket">
/// Ticket reference, if the policy requires one. For PIM-for-Groups there is no
/// ticket field on the API — the group service folds it into the justification.
/// </param>
/// <param name="IsValidationOnly">
/// When <c>true</c>, the Graph API does a dry-run: it returns the same response
/// shape (including any policy violations / approval requirement / MFA challenge)
/// but does not actually create the schedule request. Used by the "Validieren"
/// button so the user can see what activating would require before committing.
/// </param>
public sealed record ActivationRequest(
    PimEligibility Eligibility,
    TimeSpan Duration,
    string? Justification,
    TicketInfo? Ticket,
    bool IsValidationOnly = false);
