namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

public sealed class AppPathsMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("appPathsMigration").FullName;

    private string LegacyRoot => Path.Combine(_root, "legacy");

    private string TargetRoot => Path.Combine(_root, "target");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MigrateDataDirectory_MovesKnownFilesAndLogs()
    {
        Directory.CreateDirectory(Path.Combine(LegacyRoot, "logs"));
        File.WriteAllText(Path.Combine(LegacyRoot, "accounts.json"), "accounts");
        File.WriteAllText(Path.Combine(LegacyRoot, "msal.cache"), "cache");
        File.WriteAllText(Path.Combine(LegacyRoot, "logs", "old.log"), "log");

        AppPaths.MigrateDataDirectory(LegacyRoot, TargetRoot);

        Assert.Equal("accounts", File.ReadAllText(Path.Combine(TargetRoot, "accounts.json")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(TargetRoot, "msal.cache")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(TargetRoot, "logs", "old.log")));
        Assert.False(File.Exists(Path.Combine(LegacyRoot, "accounts.json")));
    }

    [Fact]
    public void MigrateDataDirectory_DoesNotOverwriteExistingTargetFiles()
    {
        Directory.CreateDirectory(LegacyRoot);
        Directory.CreateDirectory(TargetRoot);
        File.WriteAllText(Path.Combine(LegacyRoot, "settings.json"), "old");
        File.WriteAllText(Path.Combine(TargetRoot, "settings.json"), "new");

        AppPaths.MigrateDataDirectory(LegacyRoot, TargetRoot);

        Assert.Equal("new", File.ReadAllText(Path.Combine(TargetRoot, "settings.json")));
        Assert.True(File.Exists(Path.Combine(LegacyRoot, "settings.json")));
    }

    [Fact]
    public void MigrateDataDirectory_IgnoresUnknownFilesInLegacyRoot()
    {
        // The legacy root is the Velopack install root — binaries must stay put.
        Directory.CreateDirectory(LegacyRoot);
        File.WriteAllText(Path.Combine(LegacyRoot, "Update.exe"), "binary");

        AppPaths.MigrateDataDirectory(LegacyRoot, TargetRoot);

        Assert.True(File.Exists(Path.Combine(LegacyRoot, "Update.exe")));
        Assert.False(File.Exists(Path.Combine(TargetRoot, "Update.exe")));
    }

    [Fact]
    public void MigrateDataDirectory_MissingLegacyRoot_IsANoOp()
    {
        AppPaths.MigrateDataDirectory(Path.Combine(_root, "does-not-exist"), TargetRoot);

        Assert.False(Directory.Exists(TargetRoot));
    }
}
