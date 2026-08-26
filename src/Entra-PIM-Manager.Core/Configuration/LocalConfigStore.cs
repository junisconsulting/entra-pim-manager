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
    private const string TenantRegistrationsKey = "TenantAppRegistrations";

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

        var root = LoadRoot(configFilePath);
        var section = SectionOf(root);

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

        WriteRoot(configFilePath, root);
    }

    /// <summary>
    /// Adds <paramref name="registration"/> to <c>TenantAppRegistrations</c>, replacing an
    /// existing entry for the same (cloud, tenant) so the list can never hold two
    /// registrations for one tenant. The cloud is written in its canonical enum spelling;
    /// a blank label is omitted. All other keys are preserved.
    /// </summary>
    public static void SaveTenantRegistration(string configFilePath, TenantAppRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentNullException.ThrowIfNull(registration);

        if (!Enum.TryParse<EntraCloud>(registration.Cloud, ignoreCase: true, out var cloud))
        {
            throw new ArgumentException($"Unknown cloud '{registration.Cloud}'.", nameof(registration));
        }

        if (!Guid.TryParse(registration.TenantId, out var tenantId))
        {
            throw new ArgumentException($"TenantId '{registration.TenantId}' is not a GUID.", nameof(registration));
        }

        var entry = new JsonObject
        {
            ["TenantId"] = tenantId.ToString(),
            ["ClientId"] = registration.ClientId.Trim(),
            ["Cloud"] = cloud.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(registration.Label))
        {
            entry["Label"] = registration.Label.Trim();
        }

        var root = LoadRoot(configFilePath);
        var list = TenantRegistrationsOf(SectionOf(root));
        var existing = IndexOf(list, cloud, tenantId);
        if (existing >= 0)
        {
            list[existing] = entry;
        }
        else
        {
            list.Add(entry);
        }

        WriteRoot(configFilePath, root);
    }

    /// <summary>
    /// Removes the <c>TenantAppRegistrations</c> entry for (<paramref name="cloud"/>,
    /// <paramref name="tenantId"/>). No-op when there is none. All other keys are preserved.
    /// </summary>
    public static void RemoveTenantRegistration(string configFilePath, EntraCloud cloud, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        if (!Guid.TryParse(tenantId, out var tenant))
        {
            throw new ArgumentException($"TenantId '{tenantId}' is not a GUID.", nameof(tenantId));
        }

        var root = LoadRoot(configFilePath);
        var list = TenantRegistrationsOf(SectionOf(root));
        var index = IndexOf(list, cloud, tenant);
        if (index < 0)
        {
            return;
        }

        list.RemoveAt(index);
        WriteRoot(configFilePath, root);
    }

    private static JsonObject LoadRoot(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            return new JsonObject();
        }

        using var stream = File.OpenRead(configFilePath);
        return (JsonNode.Parse(stream) as JsonObject) ?? new JsonObject();
    }

    private static void WriteRoot(string configFilePath, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
        File.WriteAllText(
            configFilePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject SectionOf(JsonObject root)
    {
        if (root[EntraPimManagerOptions.SectionName] is not JsonObject section)
        {
            section = new JsonObject();
            root[EntraPimManagerOptions.SectionName] = section;
        }

        return section;
    }

    private static JsonArray TenantRegistrationsOf(JsonObject section)
    {
        if (section[TenantRegistrationsKey] is not JsonArray list)
        {
            list = new JsonArray();
            section[TenantRegistrationsKey] = list;
        }

        return list;
    }

    private static int IndexOf(JsonArray list, EntraCloud cloud, Guid tenantId)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is JsonObject entry
                && Enum.TryParse<EntraCloud>(entry["Cloud"]?.GetValue<string>() ?? nameof(EntraCloud.Global), ignoreCase: true, out var entryCloud)
                && entryCloud == cloud
                && Guid.TryParse(entry["TenantId"]?.GetValue<string>(), out var entryTenant)
                && entryTenant == tenantId)
            {
                return i;
            }
        }

        return -1;
    }
}
