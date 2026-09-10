namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;

/// <summary>
/// These arguments are typed once by an admin into a deployment tool and then run
/// unattended on every endpoint. Two failure modes have to be impossible: silently
/// falling through into a normal launch (the rollout would report success and
/// configure nothing), and persisting an unvalidated client id — the next launch
/// would fail options validation and shut down with no window to explain it.
/// </summary>
public sealed class DeploymentArgumentsTests
{
    private const string TenantId = "7b1c4d2e-0000-4000-8000-0000000000aa";
    private const string ClientId = "8f3a1c2e-0000-4000-8000-000000000001";

    [Fact]
    public void Parse_WithoutDeploymentFlags_IsNotRequested()
    {
        var result = DeploymentArguments.Parse([]);

        Assert.False(result.IsRequested);
        Assert.Null(result.Registration);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_WithRestartArgument_IsNotRequested()
    {
        // The in-app restart handover must keep working untouched.
        var result = DeploymentArguments.Parse(["--restart"]);

        Assert.False(result.IsRequested);
    }

    [Fact]
    public void Parse_WithBothIds_ReturnsAGlobalRegistration()
    {
        var result = DeploymentArguments.Parse(["--tenant-id", TenantId, "--client-id", ClientId]);

        Assert.Null(result.Error);
        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Equal(TenantId, registration.TenantId);
        Assert.Equal(ClientId, registration.ClientId);
        Assert.Equal("Global", registration.Cloud);
        Assert.Null(registration.Label);
    }

    [Fact]
    public void Parse_NormalisesTenantIdCasingAndTrimsValues()
    {
        // Must match SettingsPanelViewModel.AddTenant, or the same tenant entered
        // through the two routes would produce two entries instead of an upsert.
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId.ToUpperInvariant(), "--client-id", "  " + ClientId + "  ", "--label", "  Contoso  "]);

        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Equal(TenantId, registration.TenantId);
        Assert.Equal(ClientId, registration.ClientId);
        Assert.Equal("Contoso", registration.Label);
    }

    [Fact]
    public void Parse_WithFlagsInMixedCase_StillMatches()
    {
        var result = DeploymentArguments.Parse(["--Tenant-Id", TenantId, "--CLIENT-ID", ClientId]);

        Assert.NotNull(result.Registration);
    }

    [Fact]
    public void Parse_WithOnlyTenantId_IsRequestedAndFails()
    {
        // Requested, not ignored: a half-typed command line must abort the rollout
        // rather than start the tray app.
        var result = DeploymentArguments.Parse(["--tenant-id", TenantId]);

        Assert.True(result.IsRequested);
        Assert.Null(result.Registration);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_WithNonGuidClientId_Fails()
    {
        var result = DeploymentArguments.Parse(["--tenant-id", TenantId, "--client-id", "not-a-guid"]);

        Assert.True(result.IsRequested);
        Assert.Null(result.Registration);
        Assert.Contains("--client-id", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WithNonGuidTenantId_Fails()
    {
        var result = DeploymentArguments.Parse(["--tenant-id", "not-a-guid", "--client-id", ClientId]);

        Assert.Null(result.Registration);
        Assert.Contains("--tenant-id", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WithCloudNameInAnyCasing_ResolvesIt()
    {
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--cloud", "china"]);

        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Equal("China", registration.Cloud);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("Sovereign")]
    public void Parse_WithCloudThatIsNotAName_Fails(string cloud)
    {
        // Enum.TryParse would accept the numbers and yield an undefined value for 7.
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--cloud", cloud]);

        Assert.Null(result.Registration);
        Assert.Contains("--cloud", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WithTicketSystem_ReturnsItSeparatelyFromTheRegistration()
    {
        // It belongs in settings.json, not on the registration — the two are written
        // to different files, so the parser must not fold it into the entry.
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--ticket-system", "  ServiceNow  "]);

        Assert.Equal("ServiceNow", result.TicketSystem);
        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Null(registration.Label);
    }

    [Fact]
    public void Parse_WithoutTicketSystem_LeavesItUnset()
    {
        var result = DeploymentArguments.Parse(["--tenant-id", TenantId, "--client-id", ClientId]);

        Assert.Null(result.TicketSystem);
    }

    [Fact]
    public void Parse_WithBlankTicketSystem_LeavesItUnset()
    {
        // Null means "do not touch settings.json", so a blank value must not be
        // written as an empty ticket system.
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--ticket-system", "   "]);

        Assert.Null(result.TicketSystem);
    }

    [Fact]
    public void Parse_WithLabelAndTicketSystem_KeepsThemApart()
    {
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--label", "Contoso", "--ticket-system", "Jira"]);

        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Equal("Contoso", registration.Label);
        Assert.Equal("Jira", result.TicketSystem);
    }

    [Fact]
    public void Parse_KeepsMultiWordLabelsAndTicketSystems()
    {
        // A label like "junis DEV" arrives as one argv entry once the shell has split
        // the quoted command line. Trimming must not turn it into its first word.
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--label", "junis DEV", "--ticket-system", "Jira Service Management"]);

        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Equal("junis DEV", registration.Label);
        Assert.Equal("Jira Service Management", result.TicketSystem);
    }

    [Fact]
    public void Parse_WithOnlyTicketSystem_IsNotRequested()
    {
        // It is keyed by tenant id, so on its own there is nothing to write it against.
        var result = DeploymentArguments.Parse(["--ticket-system", "ServiceNow"]);

        Assert.False(result.IsRequested);
    }

    [Fact]
    public void Parse_WithBlankLabel_LeavesTheLabelUnset()
    {
        var result = DeploymentArguments.Parse(
            ["--tenant-id", TenantId, "--client-id", ClientId, "--label", "   "]);

        var registration = Assert.IsType<TenantAppRegistration>(result.Registration);
        Assert.Null(registration.Label);
    }

    [Fact]
    public void Parse_WithATrailingFlagAndNoValue_Fails()
    {
        var result = DeploymentArguments.Parse(["--client-id", ClientId, "--tenant-id"]);

        Assert.True(result.IsRequested);
        Assert.Null(result.Registration);
        Assert.Contains("--tenant-id", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_IgnoresUnknownArgumentsAroundTheDeploymentFlags()
    {
        var result = DeploymentArguments.Parse(
            ["--verbose", "--tenant-id", TenantId, "somefile.txt", "--client-id", ClientId]);

        Assert.NotNull(result.Registration);
    }
}
