namespace EntraPimManager.Tests.Models;

using EntraPimManager.Core.Models;

/// <summary>
/// Covers the administrative-unit scope split. A tenant-wide role and one confined to a
/// single administrative unit share a name, so getting this wrong would put them in the
/// same bucket and let a user activate the broader one believing it was the narrow one.
/// </summary>
public sealed class PimEligibilityTests
{
    private const string UnitId = "3f6b1c9e-0000-4000-8000-0000000000aa";

    [Fact]
    public void IsAdministrativeUnitScoped_TenantWideDirectoryRole_IsFalse()
    {
        Assert.False(Eligibility(PimResourceKind.DirectoryRole, "/").IsAdministrativeUnitScoped);
    }

    [Theory]
    [InlineData("/administrativeUnits/")]
    [InlineData("/AdministrativeUnits/")]
    public void IsAdministrativeUnitScoped_ScopedDirectoryRole_IsTrueAndYieldsTheUnitId(string prefix)
    {
        // Graph's casing is not something to rely on — see the entra-pim-graph-api skill.
        var eligibility = Eligibility(PimResourceKind.DirectoryRole, prefix + UnitId);

        Assert.True(eligibility.IsAdministrativeUnitScoped);
        Assert.Equal(UnitId, eligibility.AdministrativeUnitId);
    }

    [Fact]
    public void IsAdministrativeUnitScoped_AzureScopeThatLooksSimilar_IsFalse()
    {
        // An ARM scope is a different surface entirely; only directory roles carry a
        // directoryScopeId, and misreading one as an administrative unit would file an
        // Azure role under the wrong section.
        var eligibility = Eligibility(
            PimResourceKind.AzureResourceRole,
            "/subscriptions/" + UnitId + "/administrativeUnits/x");

        Assert.False(eligibility.IsAdministrativeUnitScoped);
        Assert.Null(eligibility.AdministrativeUnitId);
    }

    [Fact]
    public void NarrowedScopeId_TenantWideDirectoryRole_IsNull()
    {
        Assert.Null(Eligibility(PimResourceKind.DirectoryRole, "/").NarrowedScopeId);
    }

    [Fact]
    public void NarrowedScopeId_AppScopedDirectoryRole_IsTheScope()
    {
        // A directory role can also be narrowed to a single application. It is not an
        // administrative unit, but it is just as much "not what the tenant-wide role of
        // the same name grants" — so it must not read as unscoped either.
        var eligibility = Eligibility(PimResourceKind.DirectoryRole, "/" + UnitId);

        Assert.False(eligibility.IsAdministrativeUnitScoped);
        Assert.Equal("/" + UnitId, eligibility.NarrowedScopeId);
    }

    [Fact]
    public void NarrowedScopeId_AzureResourceRole_IsNull()
    {
        // ARM scopes carry their own readable label; this one is about directory scopes.
        Assert.Null(Eligibility(PimResourceKind.AzureResourceRole, "/subscriptions/x").NarrowedScopeId);
    }

    private static PimEligibility Eligibility(PimResourceKind kind, string scopeId) => new(
        Kind: kind,
        DisplayName: "User Administrator",
        ResourceId: "role-id",
        ScopeId: scopeId,
        PrincipalId: "oid",
        EndDateTime: null,
        IsRoleAssignableGroup: false);
}
