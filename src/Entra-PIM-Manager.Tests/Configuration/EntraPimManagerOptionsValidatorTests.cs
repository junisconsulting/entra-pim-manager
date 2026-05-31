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
