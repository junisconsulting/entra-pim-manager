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
    public void IsAdministrativeUnitScoped_ScopedDirectoryRole_IsTrue(string prefix)
    {
        // Graph's casing is not something to rely on — see the entra-pim-graph-api skill.
        var eligibility = Eligibility(PimResourceKind.DirectoryRole, prefix + UnitId);

        Assert.True(eligibility.IsAdministrativeUnitScoped);
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
    }

    [Fact]
    public void IsAdministrativeUnitScoped_AppScopedDirectoryRole_IsFalse()
    {
        // A directory role can also be confined to a single object — "/{objectId}", with
        // no path segment. That is not an administrative unit and must not be filed under
        // one; DirectoryScopeLabel is what names it.
        Assert.False(Eligibility(PimResourceKind.DirectoryRole, "/" + UnitId).IsAdministrativeUnitScoped);
    }

    [Fact]
    public void CanNarrowScope_AzureRoleOnAManagementGroup_IsTrue()
    {
        var eligibility = Eligibility(
            PimResourceKind.AzureResourceRole,
            "/providers/Microsoft.Management/managementGroups/root");

        Assert.True(eligibility.CanNarrowScope);
    }

    [Theory]
    [InlineData(PimResourceKind.AzureResourceRole, "/subscriptions/" + UnitId)]
    [InlineData(PimResourceKind.AzureResourceRole, "/subscriptions/" + UnitId + "/resourceGroups/rg-prod")]
    [InlineData(PimResourceKind.DirectoryRole, "/providers/Microsoft.Management/managementGroups/root")]
    public void CanNarrowScope_AnythingElse_IsFalse(PimResourceKind kind, string scopeId)
    {
        // A subscription could be narrowed to its resource groups; that is deliberately
        // not offered. A directory role never narrows, whatever its scope id looks like.
        Assert.False(Eligibility(kind, scopeId).CanNarrowScope);
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
