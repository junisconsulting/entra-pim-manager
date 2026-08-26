namespace EntraPimManager.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
using EntraPimManager.Core.Auth;

/// <summary>
/// Writes the user-entered configuration to <see cref="AppPaths.LocalConfigFile"/>.
/// That location is outside the Velopack-versioned install directory, so it survives
/// updates.
/// </summary>
public static class LocalConfigStore
{
    /// <summary>
    /// Stores <paramref name="clientId"/> as the App Registration for
    /// <paramref name="cloud"/> in the configuration file at <paramref name="configFilePath"/>.
    /// A blank value clears the registration: it is written as <c>""</c> rather than
    /// removed, so the shipped placeholder cannot shine through the merged
    /// configuration, and for Global the legacy <c>ClientId</c> is blanked too —
    /// otherwise the old id would come back through that fallback. Every other key
    /// — <c>AllowedTenants</c>, the other clouds' registrations — is preserved. The
    /// file and its parent directory are created if they don't exist yet.
    /// </summary>
    public static void SaveClientId(string configFilePath, EntraCloud cloud, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentNullException.ThrowIfNull(clientId);

        Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);

        JsonObject root;
        if (File.Exists(configFilePath))
        {
            using var stream = File.OpenRead(configFilePath);
            root = (JsonNode.Parse(stream) as JsonObject) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root[EntraPimManagerOptions.SectionName] is not JsonObject section)
        {
            section = new JsonObject();
            root[EntraPimManagerOptions.SectionName] = section;
        }

        if (section["AppRegistrations"] is not JsonObject registrations)
        {
            registrations = new JsonObject();
            section["AppRegistrations"] = registrations;
        }

        registrations[cloud.ToString()] = clientId.Trim();

        if (cloud == EntraCloud.Global && clientId.Trim().Length == 0 && section.ContainsKey("ClientId"))
        {
            section["ClientId"] = string.Empty;
        }

        File.WriteAllText(
            configFilePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
