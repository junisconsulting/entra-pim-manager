namespace EntraPimManager.Tests.Configuration;

using System.Text;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Configuration;

/// <summary>
/// The rules behind the junis-hosted way: which tenant counts as junis-hosted, which client id
/// an entry gets, the admin-consent link, and where the junis client id may come from. A wrong
/// answer here labels another app "junis-hosted", hands an admin a consent link for it, or writes
/// an entry the next start rejects.
/// </summary>
public sealed class JunisHostedRegistrationTests : IDisposable
{
    private const string JunisAppId = "5d0c1f2e-0000-4000-8000-0000000000f1";
    private const string OwnAppId = "8f3a1c2e-0000-4000-8000-000000000001";
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Entra PIM Manager-JunisHosted-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(JunisAppId, EntraCloud.Global, AppRegistrationHosting.JunisHosted)]
    [InlineData("{5D0C1F2E-0000-4000-8000-0000000000F1}", EntraCloud.Global, AppRegistrationHosting.JunisHosted)]
    [InlineData(JunisAppId, EntraCloud.China, AppRegistrationHosting.SelfHosted)]
    [InlineData(OwnAppId, EntraCloud.Global, AppRegistrationHosting.SelfHosted)]
    [InlineData(null, EntraCloud.Global, AppRegistrationHosting.SelfHosted)]
    public void HostingOf_IsJunisHostedOnlyForTheJunisIdInTheGlobalCloud(string? clientId, EntraCloud cloud, AppRegistrationHosting expected)
        => Assert.Equal(expected, Junis().HostingOf(cloud, clientId));

    [Fact]
    public void HostingOf_WithoutAShippedJunisId_IsAlwaysSelfHosted()
    {
        // An empty JunisHostedClientId is how a build ships without the feature.
        var options = new EntraPimManagerOptions();

        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingOf(EntraCloud.Global, JunisAppId));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingOf(EntraCloud.Global, string.Empty));
    }

    [Fact]
    public void HostingFor_FollowsTheTenantsConfiguredEntry()
    {
        var options = Junis();
        options.TenantAppRegistrations.Add(new TenantAppRegistration { TenantId = TenantA, ClientId = JunisAppId, Cloud = "Global" });

        Assert.Equal(AppRegistrationHosting.JunisHosted, options.HostingFor(EntraCloud.Global, TenantA));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingFor(EntraCloud.Global, Guid.NewGuid().ToString()));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData(null)]
    public void UseJunisHostedClientId_AnythingButAGuid_CountsAsNone(string? value)
    {
        // A junis-hosted entry copies this value into ClientId; the validator would reject a
        // non-GUID there at the next start and close the app without a window.
        var options = new EntraPimManagerOptions();

        options.UseJunisHostedClientId(value);

        Assert.Null(options.JunisHostedClientId);
    }

    [Fact]
    public void UseJunisHostedClientId_StoresTheCanonicalForm()
    {
        var options = new EntraPimManagerOptions();

        options.UseJunisHostedClientId("{5D0C1F2E-0000-4000-8000-0000000000F1}");

        Assert.Equal(JunisAppId, options.JunisHostedClientId);
    }

    [Theory]
    [InlineData(AppRegistrationHosting.JunisHosted, EntraCloud.Global, "ignored", JunisAppId)]
    [InlineData(AppRegistrationHosting.JunisHosted, EntraCloud.China, OwnAppId, null)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "  " + OwnAppId + " ", OwnAppId)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.China, OwnAppId, OwnAppId)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "", null)]
    [InlineData(AppRegistrationHosting.SelfHosted, EntraCloud.Global, "not-a-guid", null)]
    public void ResolveClientId_FollowsTheChosenWay(AppRegistrationHosting hosting, EntraCloud cloud, string typed, string? expected)
        => Assert.Equal(expected, Junis().ResolveClientId(hosting, cloud, typed));

    [Fact]
    public void ResolveClientId_JunisHostedWithoutAShippedId_IsInvalid()
        => Assert.Null(new EntraPimManagerOptions().ResolveClientId(AppRegistrationHosting.JunisHosted, EntraCloud.Global, null));

    [Fact]
    public void JunisAdminConsentUrl_IsTheDocumentedLinkWithAnEncodedRedirectUri()
    {
        var url = Junis().JunisAdminConsentUrl(EntraCloud.Global, TenantA.ToUpperInvariant());

        Assert.Equal(
            $"https://login.microsoftonline.com/{TenantA}/adminconsent?client_id={JunisAppId}"
            + $"&redirect_uri=ms-appx-web%3A%2F%2Fmicrosoft.aad.brokerplugin%2F{JunisAppId}",
            url);
    }

    [Fact]
    public void JunisAdminConsentUrl_OutsideTheGlobalCloudOrWithoutAJunisId_IsNull()
    {
        Assert.Null(Junis().JunisAdminConsentUrl(EntraCloud.China, TenantA));
        Assert.Null(Junis().JunisAdminConsentUrl(EntraCloud.Global, "not-a-guid"));
        Assert.Null(new EntraPimManagerOptions().JunisAdminConsentUrl(EntraCloud.Global, TenantA));
    }

    [Fact]
    public void Bind_AJunisIdInAnyConfigurationLayer_IsIgnored()
    {
        // The per-user appsettings.local.json is writable by anything running as the user.
        // It must not be able to point "junis-hosted", and the consent link, at another app.
        const string layer = """
            { "EntraPimManager": { "Scopes": [ "User.Read" ], "JunisHostedClientId": "8f3a1c2e-0000-4000-8000-000000000001" } }
            """;

        var options = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(layer)))
            .Build()
            .GetSection(EntraPimManagerOptions.SectionName)
            .Get<EntraPimManagerOptions>()!;

        Assert.Null(options.JunisHostedClientId);
    }

    [Fact]
    public void ReadShippedJunisHostedClientId_ReadsTheKey()
    {
        var path = WriteFile("""{ "EntraPimManager": { "JunisHostedClientId": "5d0c1f2e-0000-4000-8000-0000000000f1" } }""");

        Assert.Equal(JunisAppId, EntraPimManagerOptions.ReadShippedJunisHostedClientId(path));
    }

    [Theory]
    [InlineData("""{ "EntraPimManager": { "JunisHostedClientId": 42 } }""")]
    [InlineData("""{ "EntraPimManager": { } }""")]
    [InlineData("{ not json")]
    public void ReadShippedJunisHostedClientId_BrokenOrMissingValue_IsNullNotAnError(string content)
        => Assert.Null(EntraPimManagerOptions.ReadShippedJunisHostedClientId(WriteFile(content)));

    [Fact]
    public void ReadShippedJunisHostedClientId_MissingFile_IsNull()
        => Assert.Null(EntraPimManagerOptions.ReadShippedJunisHostedClientId(Path.Combine(_directory, "missing.json")));

    [Fact]
    public void CommittedAppSettings_JunisIdIsEmptyOrAGuidAndTheFileValidates()
    {
        // The real file the app ships, linked into the test output. Until the release gate in
        // docs/adr/0002 is met the id stays empty; once set it must be a GUID, or junis-hosted
        // silently disappears from the build.
        var path = Path.Combine(AppContext.BaseDirectory, "Shipped", "appsettings.json");
        var shipped = EntraPimManagerOptions.ReadShippedJunisHostedClientId(path);
        Assert.True(string.IsNullOrEmpty(shipped) || Guid.TryParse(shipped, out _), $"JunisHostedClientId '{shipped}' is neither empty nor a GUID.");

        // The configuration system tolerates comments and trailing commas; this reader and the
        // install script's ConvertFrom-Json do not. If they disagree, junis-hosted would vanish
        // from the build without a word.
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        Assert.Equal(configuration["EntraPimManager:JunisHostedClientId"] ?? string.Empty, shipped ?? string.Empty);

        var options = configuration.GetSection(EntraPimManagerOptions.SectionName).Get<EntraPimManagerOptions>()!;
        options.UseJunisHostedClientId(shipped);

        Assert.True(new EntraPimManagerOptionsValidator().Validate(null, options).Succeeded);
        Assert.Empty(options.TenantAppRegistrations);
    }

    [Fact]
    public void CommittedAppSettings_ASelfHostedEntryLayeredOnTop_BindsUnchanged()
    {
        // The per-user file sits on top of the shipped one; the new key must not disturb it.
        const string perUser = """
            { "EntraPimManager": { "TenantAppRegistrations": [ { "TenantId": "7b1c4d2e-0000-4000-8000-0000000000aa", "ClientId": "8f3a1c2e-0000-4000-8000-000000000001", "Label": "Contoso" } ] } }
            """;

        var options = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Shipped", "appsettings.json"))
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(perUser)))
            .Build()
            .GetSection(EntraPimManagerOptions.SectionName)
            .Get<EntraPimManagerOptions>()!;

        var entry = Assert.Single(options.TenantAppRegistrations);
        Assert.Equal((TenantA, OwnAppId, "Global", "Contoso"), (entry.TenantId, entry.ClientId, entry.Cloud, entry.Label));
        Assert.Equal(AppRegistrationHosting.SelfHosted, options.HostingFor(EntraCloud.Global, TenantA));
    }

    private static EntraPimManagerOptions Junis()
    {
        var options = new EntraPimManagerOptions();
        options.UseJunisHostedClientId(JunisAppId);
        return options;
    }

    private string WriteFile(string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}
