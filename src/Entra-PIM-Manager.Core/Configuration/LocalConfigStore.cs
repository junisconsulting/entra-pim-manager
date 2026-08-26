namespace EntraPimManager.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
using EntraPimManager.Core.Auth;

/// <summary>
/// Writes the user-entered configuration to <see cref="AppPaths.LocalConfigFile"/>.
/// That location is outside the Velopack-versioned install directory, so it survives
/// updates. Every write is a read-modify-write that preserves unrelated keys.
/// </summary>
public static class LocalConfigStore
{
    private const string TenantRegistrationsKey = "TenantAppRegistrations";

    /// <summary>
    /// Adds <paramref name="registration"/> to <c>TenantAppRegistrations</c>, replacing an
    /// existing entry for the same (cloud, tenant) so the list can never hold two
    /// registrations for one tenant. The cloud is written in its canonical enum spelling;
    /// a blank label is omitted. The file and its parent directory are created if needed.
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

        var root = LoadRoot(configFilePath);
        var list = TenantRegistrationsOf(SectionOf(root));
        Upsert(list, cloud, tenantId, registration.ClientId, registration.Label);
        WriteRoot(configFilePath, root);
    }

    /// <summary>
    /// Removes the <c>TenantAppRegistrations</c> entry for (<paramref name="cloud"/>,
    /// <paramref name="tenantId"/>). No-op when there is none.
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

    internal static JsonObject LoadRoot(string configFilePath)
    {
        if (!File.Exists(configFilePath))
        {
            return new JsonObject();
        }

        using var stream = File.OpenRead(configFilePath);
        return (JsonNode.Parse(stream) as JsonObject) ?? new JsonObject();
    }

    internal static void WriteRoot(string configFilePath, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
        File.WriteAllText(
            configFilePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static JsonObject SectionOf(JsonObject root)
    {
        if (root[EntraPimManagerOptions.SectionName] is not JsonObject section)
        {
            section = new JsonObject();
            root[EntraPimManagerOptions.SectionName] = section;
        }

        return section;
    }

    internal static JsonArray TenantRegistrationsOf(JsonObject section)
    {
        if (section[TenantRegistrationsKey] is not JsonArray list)
        {
            list = new JsonArray();
            section[TenantRegistrationsKey] = list;
        }

        return list;
    }

    /// <summary>Adds or replaces the entry for (cloud, tenant).</summary>
    internal static void Upsert(JsonArray list, EntraCloud cloud, Guid tenantId, string clientId, string? label)
    {
        var entry = new JsonObject
        {
            ["TenantId"] = tenantId.ToString(),
            ["ClientId"] = clientId.Trim(),
            ["Cloud"] = cloud.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(label))
        {
            entry["Label"] = label.Trim();
        }

        var existing = IndexOf(list, cloud, tenantId);
        if (existing >= 0)
        {
            list[existing] = entry;
        }
        else
        {
            list.Add(entry);
        }
    }

    internal static int IndexOf(JsonArray list, EntraCloud cloud, Guid tenantId)
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
