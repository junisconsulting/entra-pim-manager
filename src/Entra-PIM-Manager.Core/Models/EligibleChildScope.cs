namespace EntraPimManager.Core.Models;

using System.Text.Json.Serialization;

/// <summary>
/// A management group or subscription beneath an Azure eligibility's scope on which
/// the role can be activated instead of on the whole scope — one row of ARM's
/// <c>eligibleChildResources</c>. Also the persisted shape inside a
/// <see cref="ScopeFavorite"/>.
/// </summary>
/// <param name="Id">ARM scope id, passed verbatim as the activation scope.</param>
/// <param name="Name">Display name.</param>
/// <param name="Type">ARM resource type as reported: <c>managementgroup</c> or <c>subscription</c>.</param>
/// <param name="ParentName">
/// Display name of the management group this scope sits directly under, or <c>null</c>
/// for a direct child of the eligibility's own scope. Display only — the flat picker
/// reads "under Landing zones" instead of showing a tree.
/// </param>
public sealed record EligibleChildScope(string Id, string Name, string Type, string? ParentName = null)
{
    /// <summary>True for a management group.</summary>
    [JsonIgnore]
    public bool IsManagementGroup => string.Equals(Type, "managementgroup", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a subscription.</summary>
    [JsonIgnore]
    public bool IsSubscription => string.Equals(Type, "subscription", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Management group" or "Subscription".</summary>
    [JsonIgnore]
    public string KindLabel => KindLabelFor(Type);

    /// <summary>
    /// The same "Subscription: lz-prod" form the eligibility list uses, so an
    /// activation narrowed to this scope reads like every other row.
    /// </summary>
    [JsonIgnore]
    public string ScopeLabel => $"{KindLabel}: {Name}";

    /// <summary>
    /// Turns an ARM scope type into words — the one place that mapping lives, so a
    /// narrowed activation's row can never drift from the eligibility list's.
    /// </summary>
    public static string KindLabelFor(string? type) => type?.ToLowerInvariant() switch
    {
        "subscription" => "Subscription",
        "resourcegroup" => "Resource group",
        "managementgroup" => "Management group",
        _ => "Resource",
    };
}
