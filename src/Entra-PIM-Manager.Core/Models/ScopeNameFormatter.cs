namespace EntraPimManager.Core.Models;

/// <summary>
/// Turns a scope into the shortest text that still says where a role applies.
/// Counterpart to the <c>"Kind: Name"</c> labels the read paths build, for the places
/// that already say the word "Scope" themselves and only need the name.
/// </summary>
public static class ScopeNameFormatter
{
    /// <summary>
    /// The name out of a <c>"Kind: Name"</c> scope label, or the scope id's last
    /// segment when there is no label.
    /// </summary>
    /// <remarks>
    /// Splitting the label back apart is deterministic because both producers format
    /// it the same way (<see cref="EligibleChildScope.ScopeLabel"/> and the ARM read
    /// path), and it beats threading name and type through every model in between.
    /// A name that contains ": " itself survives: only the first separator counts.
    /// </remarks>
    public static string NameOf(string? scopeLabel, string scopeId)
    {
        if (!string.IsNullOrWhiteSpace(scopeLabel))
        {
            var separator = scopeLabel.IndexOf(": ", StringComparison.Ordinal);
            return separator >= 0 ? scopeLabel[(separator + 2)..] : scopeLabel;
        }

        return LastSegment(scopeId) ?? scopeId;
    }

    /// <summary>
    /// What to show after "Scope:". A subscription or management group is its own
    /// name; a resource group is qualified by the subscription it lives in, because
    /// an "rg-prod" exists in half of them.
    /// </summary>
    /// <param name="scopeId">ARM scope id, or any other scope identifier.</param>
    /// <param name="scopeLabel">The <c>"Kind: Name"</c> label the read path built.</param>
    /// <param name="subscriptionName">
    /// Display name of the subscription the resource group lives in, when it is
    /// known. ARM does not return it on a resource-group-scoped assignment — only
    /// the subscription's id is in the scope path — so the caller resolves it from
    /// whatever else it has seen, and the id stands in when nothing has.
    /// </param>
    public static string Describe(string scopeId, string? scopeLabel, string? subscriptionName)
    {
        var name = NameOf(scopeLabel, scopeId);
        if (!IsResourceGroup(scopeId))
        {
            return name;
        }

        var subscription = string.IsNullOrWhiteSpace(subscriptionName)
            ? SubscriptionIdOf(scopeId)
            : subscriptionName;

        return string.IsNullOrEmpty(subscription) ? name : $"{subscription}/{name}";
    }

    /// <summary>
    /// The subscription id out of an ARM scope path, or <c>null</c> when the scope is
    /// not under one (a management group, or a Graph scope).
    /// </summary>
    public static string? SubscriptionIdOf(string? scopeId)
    {
        var segments = Segments(scopeId);
        return segments.Length >= 2
            && string.Equals(segments[0], "subscriptions", StringComparison.OrdinalIgnoreCase)
            ? segments[1]
            : null;
    }

    /// <summary>
    /// The subscription id when the scope <em>is</em> that subscription rather than
    /// something inside it — the rows a caller can harvest names from.
    /// </summary>
    public static string? SubscriptionScopeOf(string? scopeId)
    {
        var segments = Segments(scopeId);
        return segments.Length == 2
            && string.Equals(segments[0], "subscriptions", StringComparison.OrdinalIgnoreCase)
            ? segments[1]
            : null;
    }

    /// <summary>
    /// True for a scope that is a resource group itself. A resource *inside* one is
    /// deliberately excluded: its own name is what identifies it, and ARM already
    /// labels it "Resource: …".
    /// </summary>
    private static bool IsResourceGroup(string? scopeId)
    {
        var segments = Segments(scopeId);
        return segments.Length == 4
            && string.Equals(segments[0], "subscriptions", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[2], "resourceGroups", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Segments(string? scopeId)
        => string.IsNullOrWhiteSpace(scopeId)
            ? []
            : scopeId.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string? LastSegment(string? scopeId)
        => Segments(scopeId) is { Length: > 0 } segments ? segments[^1] : null;
}
