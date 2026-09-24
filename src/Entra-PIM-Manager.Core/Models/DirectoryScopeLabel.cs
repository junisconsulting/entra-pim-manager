namespace EntraPimManager.Core.Models;

/// <summary>
/// Names the scope of a directory-role assignment, so an administrative-unit or
/// single-object assignment cannot be mistaken for a tenant-wide one.
/// </summary>
/// <remarks>
/// A directory role carries one of three scopes, and only its id comes back on the
/// schedule instances: <c>"/"</c> for the tenant, <c>"/administrativeUnits/{id}"</c>
/// for the members of an administrative unit, and <c>"/{objectId}"</c> for a single
/// directory object — typically an app registration or service principal. The last
/// one has no path segment naming its kind; that is Microsoft's design, not an
/// omission, and it is why the expanded object's OData type has to supply the word.
/// </remarks>
public static class DirectoryScopeLabel
{
    private const string AdministrativeUnitPrefix = "/administrativeUnits/";

    /// <summary>
    /// The <c>"Kind: Name"</c> label for a scope, or <c>null</c> for a tenant-wide
    /// role — which needs no label, because tenant-wide is what a role without one
    /// has always meant.
    /// </summary>
    /// <param name="directoryScopeId">The <c>directoryScopeId</c> as received.</param>
    /// <param name="odataType">
    /// <c>@odata.type</c> of the expanded <c>directoryScope</c>, e.g.
    /// <c>#microsoft.graph.servicePrincipal</c>. <c>null</c> when the expansion was
    /// not asked for or came back empty.
    /// </param>
    /// <param name="displayName">
    /// Display name of the expanded scope. <c>null</c> falls back to the id — worth
    /// little to read, but it still says the role is <em>not</em> tenant-wide, which
    /// is the part that changes what the holder can do.
    /// </param>
    public static string? For(string? directoryScopeId, string? odataType, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(directoryScopeId) || directoryScopeId == "/")
        {
            return null;
        }

        var id = directoryScopeId.StartsWith(AdministrativeUnitPrefix, StringComparison.OrdinalIgnoreCase)
            ? directoryScopeId[AdministrativeUnitPrefix.Length..]
            : directoryScopeId.TrimStart('/');

        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return $"{KindOf(directoryScopeId, odataType)}: {name}";
    }

    /// <summary>
    /// The word in front of the name. The administrative-unit path says it outright;
    /// otherwise only the expanded object's type can, and without it "Directory
    /// object" is as far as the data goes.
    /// </summary>
    private static string KindOf(string directoryScopeId, string? odataType)
    {
        if (directoryScopeId.StartsWith(AdministrativeUnitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "Administrative unit";
        }

        var type = odataType?.Split('.').LastOrDefault();
        return type?.ToLowerInvariant() switch
        {
            "administrativeunit" => "Administrative unit",
            "application" => "App registration",
            "serviceprincipal" => "Enterprise application",
            "group" => "Group",
            "device" => "Device",
            "user" => "User",
            _ => "Directory object",
        };
    }
}
