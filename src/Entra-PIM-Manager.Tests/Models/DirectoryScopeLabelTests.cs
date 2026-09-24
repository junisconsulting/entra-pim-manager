namespace EntraPimManager.Tests.Models;

using EntraPimManager.Core.Models;
using Xunit;

public class DirectoryScopeLabelTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    public void For_TenantScope_HasNoLabel(string? scopeId)
        => Assert.Null(DirectoryScopeLabel.For(scopeId, null, null));

    [Fact]
    public void For_AdministrativeUnit_IsNamedFromThePath()
    {
        // The path segment says what it is even when the expansion brought nothing.
        var label = DirectoryScopeLabel.For(
            "/administrativeUnits/11111111-1111-1111-1111-111111111111",
            "#microsoft.graph.administrativeUnit",
            "Seattle Admin Unit");

        Assert.Equal("Administrative unit: Seattle Admin Unit", label);
    }

    [Fact]
    public void For_SingleObject_TakesItsKindFromTheExpandedType()
    {
        // "/{objectId}" carries no segment naming the kind — by Microsoft's design.
        var label = DirectoryScopeLabel.For(
            "/22222222-2222-2222-2222-222222222222",
            "#microsoft.graph.servicePrincipal",
            "Contoso Payroll API");

        Assert.Equal("Enterprise application: Contoso Payroll API", label);
    }

    [Fact]
    public void For_AppRegistration_IsNamedAsSuch()
        => Assert.Equal(
            "App registration: f/128 Filter Photos",
            DirectoryScopeLabel.For("/33333333-3333-3333-3333-333333333333", "#microsoft.graph.application", "f/128 Filter Photos"));

    [Fact]
    public void For_WithoutExpansion_StillSaysItIsNotTenantWide()
    {
        // The whole point: a scoped role must never read like a tenant-wide one, even
        // when the name could not be resolved.
        var label = DirectoryScopeLabel.For("/administrativeUnits/44444444-4444-4444-4444-444444444444", null, null);

        Assert.Equal("Administrative unit: 44444444-4444-4444-4444-444444444444", label);
    }

    [Fact]
    public void For_UnknownExpandedType_FallsBackToDirectoryObject()
        => Assert.Equal(
            "Directory object: Something",
            DirectoryScopeLabel.For("/55555555-5555-5555-5555-555555555555", "#microsoft.graph.somethingNew", "Something"));
}
