namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>The four kinds of eligibility a tenant group separates into.</summary>
public enum EligibilitySectionKind
{
    /// <summary>Directory roles that apply across the whole directory.</summary>
    DirectoryRole,

    /// <summary>Directory roles confined to one administrative unit.</summary>
    AdministrativeUnit,

    /// <summary>PIM-for-Groups membership and ownership.</summary>
    Group,

    /// <summary>Azure RBAC roles, activated through Azure Resource Manager.</summary>
    AzureResource,
}

/// <summary>
/// One kind of eligibility inside a tenant group — the level that keeps a large
/// directory readable.
/// </summary>
/// <remarks>
/// Without it a tenant is a flat list: an infrastructure team eligible across a large
/// estate opens the popup to several hundred rows and scrolls past all of them to reach
/// the next tenant. Four collapsible lines per tenant answer "what do I have here" before
/// anyone has to scroll or type.
/// <para/>
/// Administrative-unit roles get their own section rather than a note on a directory-role
/// row: the scope is not directory-wide, and a section header states that once instead of
/// each row having to.
/// <para/>
/// The rows are the very same view models the tenant group holds in its canonical
/// <see cref="TenantEligibilityGroup.Items"/> list, so filtering and the active-marking
/// pass keep operating on one set of objects.
/// </remarks>
public sealed partial class EligibilitySection : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// How many eligibilities in this section are active right now. Surfaced on the
    /// collapsed header — folding a section away must not fold away the fact that
    /// something behind it is granting access.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    [NotifyPropertyChangedFor(nameof(ActiveLabel))]
    private int _activeCount;

    public EligibilitySection(EligibilitySectionKind kind)
    {
        Kind = kind;
    }

    /// <summary>Which kind of eligibility this section holds.</summary>
    public EligibilitySectionKind Kind { get; }

    /// <summary>
    /// Header text. Upper case here rather than in the view: Avalonia has no
    /// text-transform, and the caption style these headers share with PINNED,
    /// RECENT and ELIGIBILITIES is what keeps them from competing with the rows
    /// underneath them.
    /// </summary>
    public string Title => Kind switch
    {
        EligibilitySectionKind.DirectoryRole => "DIRECTORY ROLES",
        EligibilitySectionKind.AdministrativeUnit => "ADMINISTRATIVE UNITS",
        EligibilitySectionKind.Group => "GROUPS",
        EligibilitySectionKind.AzureResource => "AZURE ROLES",
        _ => string.Empty,
    };

    /// <summary>
    /// The plain rows of this section — everything not folded into a role node. For
    /// <see cref="EligibilitySectionKind.AzureResource"/> that is the roles held on a
    /// single scope; the rest of that section lives in <see cref="RoleGroups"/>.
    /// </summary>
    public ObservableCollection<EligibilityItemViewModel> Items { get; } = [];

    /// <summary>
    /// Azure roles held on more than one scope, one collapsible node each. Empty for
    /// every other section.
    /// </summary>
    public ObservableCollection<AzureRoleGroup> RoleGroups { get; } = [];

    /// <summary>True while at least one eligibility in this section is active.</summary>
    public bool HasActive => ActiveCount > 0;

    /// <summary>Badge text for the active entries, e.g. <c>"2 active"</c>.</summary>
    public string ActiveLabel => $"{ActiveCount} active";

    [RelayCommand]
    private void ToggleExpansion() => IsExpanded = !IsExpanded;
}
