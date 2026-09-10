namespace EntraPimManager.AppAvalonia.ViewModels;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>
/// One activation the panel submitted: who, what, where, and what the service
/// answered. Self-contained, so the shell needs nothing from the panel — which may
/// already be showing another role by the time the shell reads it.
/// </summary>
/// <param name="Account">The enrollment the activation ran under.</param>
/// <param name="Request">
/// The request as sent. Its eligibility is the list row; its
/// <see cref="ActivationRequest.TargetScope"/> is set when the activation was narrowed.
/// </param>
/// <param name="Result">The service's answer.</param>
public sealed record ActivationOutcome(SignedInAccount Account, ActivationRequest Request, ActivationResult Result)
{
    /// <summary>True when the activation went to a scope beneath the eligibility's.</summary>
    public bool IsNarrowed => Request.TargetScope is not null;

    /// <summary>
    /// The eligibility as activated: the row's own, or a copy carrying the narrowed
    /// scope and its label — what the pending row shows.
    /// </summary>
    public PimEligibility Target => Request.TargetScope is { } scope
        ? Request.Eligibility with { ScopeId = scope.Id, ScopeLabel = scope.ScopeLabel }
        : Request.Eligibility;
}
