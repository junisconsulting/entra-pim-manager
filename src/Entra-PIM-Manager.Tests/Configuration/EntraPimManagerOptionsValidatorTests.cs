namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Options;

public sealed class EntraPimManagerOptionsValidatorTests
{
    [Fact]
    public void Validate_WithCompleteConfiguration_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();

        var result = validator.Validate(name: null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithPlaceholderClientId_Succeeds()
    {
        // ClientId is intentionally lenient: the first-run UI catches an
        // empty or non-GUID ClientId via ShellViewModel.NeedsConfiguration
        // so the app starts and prompts the user to configure it.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.ClientId = "YOUR-CLIENT-ID-HERE";

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithEmptyClientId_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.ClientId = string.Empty;

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithPerCloudRegistrations_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AppRegistrations = new()
        {
            ["Global"] = "8f3a1c2e-0000-4000-8000-000000000001",
            ["China"] = "8f3a1c2e-0000-4000-8000-000000000002",
        };

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithUnknownCloudName_Fails()
    {
        // A typo here would silently leave that cloud unconfigured, and the user
        // has no way to see it from the UI — so fail loudly at startup instead.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AppRegistrations = new() { ["Chnia"] = "8f3a1c2e-0000-4000-8000-000000000002" };

        var result = validator.Validate(name: null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("Chnia", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("YOUR-CLIENT-ID-HERE")]
    [InlineData("")]
    public void Validate_WithUnusableRegistrationValue_Succeeds(string clientId)
    {
        // Same leniency as the legacy ClientId, and for the same reason: the
        // shipped appsettings.json carries the placeholder, and a cloud the user
        // doesn't use stays blank. ValidateOnStart failing here would shut the app
        // down (App.axaml.cs) instead of showing the first-run CTA. Unusable values
        // are filtered by ConfiguredClouds, not rejected here.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AppRegistrations = new() { ["Global"] = clientId };

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShippedAppSettingsShape_Succeeds()
    {
        // Guards first-run end to end: this is verbatim what
        // src/Entra-PIM-Manager.App.Avalonia/appsettings.json ships.
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.ClientId = string.Empty;
        options.AppRegistrations = new() { ["Global"] = "YOUR-CLIENT-ID-HERE", ["China"] = string.Empty };

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
    public void Validate_WithEmptyAllowedTenants_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AllowedTenants = [];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithValidAllowedTenants_Succeeds()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AllowedTenants = ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithNonGuidAllowedTenant_Fails()
    {
        var validator = new EntraPimManagerOptionsValidator();
        var options = ValidOptions();
        options.AllowedTenants = ["not-a-guid"];

        var result = validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("AllowedTenants", StringComparison.Ordinal));
    }

    private static EntraPimManagerOptions ValidOptions() => new()
    {
        ClientId = "22222222-2222-2222-2222-222222222222",
        Scopes = ["User.Read"],
    };
}
