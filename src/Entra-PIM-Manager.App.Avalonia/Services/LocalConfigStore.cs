namespace EntraPimManager.AppAvalonia.Services;

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Reads and writes the user-entered configuration at
/// <c>%LocalAppData%\Entra-PIM-Manager\appsettings.local.json</c>. This location is
/// outside the Velopack-versioned install directory, so it survives updates.
/// </summary>
public static class LocalConfigStore
{
    /// <summary>Full path of the user-entered configuration file.</summary>
    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Entra-PIM-Manager",
        "appsettings.local.json");

    /// <summary>
    /// Writes the user-configured ClientId into the local configuration file.
    /// Existing keys (e.g. <c>AllowedTenants</c>) are preserved — only the
    /// <c>EntraPimManager.ClientId</c> entry is overwritten. The file is created
    /// (with its parent directory) if it doesn't exist yet.
    /// </summary>
    public static void SaveClientId(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);

        JsonObject root;
        if (File.Exists(ConfigFilePath))
        {
            using var stream = File.OpenRead(ConfigFilePath);
            root = (JsonNode.Parse(stream) as JsonObject) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["EntraPimManager"] is not JsonObject section)
        {
            section = new JsonObject();
            root["EntraPimManager"] = section;
        }

        section["ClientId"] = clientId;

        File.WriteAllText(
            ConfigFilePath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
