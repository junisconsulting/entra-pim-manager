namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Options;

public sealed class EntraPimManagerOptionsValidatorTests
{
    private const string TenantA = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string AppId = "8f3a1c2e-0000-4000-8000-0000000000a1";

    [Fact]
    public void Validate_WithCompleteConfiguration_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();

        var result = validator.Validate(name: null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShippedAppSettingsShape_Succeeds()
    {
        // Guards first-run end to end: the shipped appsettings.json carries only
        // the scopes. An empty list must boot into the first-run CTA, not fail
        // ValidateOnStart (which shuts the app down).
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.TenantAppRegistrations = [];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithNoScopes_Fails()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.Scopes = [];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Scopes", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithUnknownCloud_Fails()
    {
        // A typo here would silently leave that entry unusable, and the user has
        // no way to see it from the UI — so fail loudly at startup instead.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.TenantAppRegistrations = [Pinned(TenantA, AppId, "Chnia")];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("TenantAppRegistrations[0]", StringComparison.Ordinal) && f.Contains("Chnia", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-a-guid", AppId, "TenantId")]
    [InlineData(TenantA, "YOUR-CLIENT-ID-HERE", "ClientId")]
    [InlineData(TenantA, "", "ClientId")]
    public void Validate_WithNonGuidField_Fails(string tenantId, string clientId, string field)
    {
        // There is no shipped placeholder and no "leave blank" case — an entry
        // only exists because someone added it, and a malformed tenant id would
        // silently never match a sign-in.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.TenantAppRegistrations = [Pinned(tenantId, clientId, "Global")];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithDuplicateTenant_Fails()
    {
        // ClientIdFor takes the first match; a second entry for the same tenant
        // would be dead configuration that looks alive.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.TenantAppRegistrations =
        [
            Pinned(TenantA, AppId, "Global"),
            Pinned(TenantA.ToUpperInvariant(), "8f3a1c2e-0000-4000-8000-0000000000a2", "global"),
        ];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("TenantAppRegistrations[1]", StringComparison.Ordinal) && f.Contains("duplicates", StringComparison.Ordinal));
    }

    private static EntraPimManagerOptions ValidOptions() => new()
    {
        TenantAppRegistrations = [Pinned(TenantA, AppId, "Global")],
        Scopes = ["User.Read"],
    };

    private static TenantAppRegistration Pinned(string tenantId, string clientId, string cloud)
        => new() { TenantId = tenantId, ClientId = clientId, Cloud = cloud };
}
