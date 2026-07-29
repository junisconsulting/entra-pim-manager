namespace EntraPimManager.Core.Graph;

using Microsoft.Kiota.Abstractions;

/// <summary>
/// Kiota request option that makes <see cref="MsalAuthProvider"/> acquire the
/// token for this specific request with a Conditional Access claims challenge.
/// Used proactively for PIM activations whose policy demands an authentication
/// context — that surface rejects an insufficient token with HTTP 400
/// (<c>RoleAssignmentRequestAcrsValidationFailed</c>) instead of a 401 claims
/// challenge, so the reactive <see cref="ClaimsChallengeHandler"/> never fires.
/// </summary>
public sealed class AuthContextRequestOption : IRequestOption
{
    /// <summary>The decoded claims-challenge JSON to pass to MSAL.</summary>
    public required string ClaimsJson { get; init; }
}
