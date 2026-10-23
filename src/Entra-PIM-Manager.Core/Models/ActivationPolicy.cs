namespace EntraPimManager.Core.Models;

/// <summary>
/// The activation constraints parsed from a role/group PIM policy. Defaults are
/// applied when a rule is absent — see the per-property defaults below.
/// </summary>
public sealed record ActivationPolicy
{
    /// <summary>Maximum duration the user may request. Default: 8 hours.</summary>
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Whether a justification text is required. Default: <c>true</c> (safer).</summary>
    public bool RequiresJustification { get; init; } = true;

    /// <summary>Whether ticket information is required. Default: <c>false</c>.</summary>
    public bool RequiresTicketInfo { get; init; }

    /// <summary>Whether the token must satisfy an MFA claim. Default: <c>false</c>.</summary>
    public bool RequiresMfa { get; init; }

    /// <summary>Whether the activation must be approved. Default: <c>false</c>.</summary>
    public bool RequiresApproval { get; init; }

    /// <summary>Whether a Conditional Access authentication context is required. Default: <c>false</c>.</summary>
    public bool RequiresAuthContext { get; init; }

    /// <summary>The authentication context claim value (e.g. <c>c1</c>), if any.</summary>
    public string? AuthContextClaim { get; init; }

    /// <summary>
    /// Whether the role's authentication-context rule came back, switched on or off. Every PIM
    /// policy carries one, so <c>false</c> means the read failed or found nothing (normal for an
    /// eligible-only user on an Azure resource role) and <see cref="RequiresAuthContext"/> is a
    /// default that says nothing about the role.
    /// </summary>
    public bool AuthContextKnown { get; init; }

    /// <summary>
    /// The role is known to activate without an authentication context, so nothing asks the user
    /// to sign in again. PIM's own MFA setting does not count: an earlier MFA in the session
    /// satisfies it.
    /// </summary>
    public bool LacksFreshSignIn => AuthContextKnown && !RequiresAuthContext;
}
