namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Backing model for the standalone expiry-alert window. The shell mutates this
/// single instance on every countdown tick (see
/// <see cref="ShellViewModel.EvaluateExpiryWarnings"/>) so the surfaced countdown
/// ticks live; the window binds to it directly. UI text is English per the
/// project conventions.
/// </summary>
public sealed partial class ExpiryAlertViewModel : ObservableObject
{
    /// <summary>Role or group whose activation is closest to expiring.</summary>
    [ObservableProperty]
    private string _resourceName = string.Empty;

    /// <summary>Tenant · account context line, so the user knows which enrollment is affected.</summary>
    [ObservableProperty]
    private string _contextLabel = string.Empty;

    /// <summary>
    /// Scope of an Azure resource role ("Subscription: prod"), <c>null</c> for the
    /// Graph kinds. Without it "Owner" names nothing: the same role can be active on
    /// several scopes at once, and the alert would not say which one is running out.
    /// </summary>
    [ObservableProperty]
    private string? _scopeLabel;

    /// <summary>Pre-formatted remaining time, e.g. <c>"4m"</c> or <c>"45s"</c>.</summary>
    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>
    /// Remaining time on the most-urgent assignment. Drives the countdown colour
    /// through <see cref="RemainingTimeToBrushConverter"/> exactly like the active rows.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _remainingTime;

    /// <summary>How many further assignments are also inside the warning window right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdditional))]
    [NotifyPropertyChangedFor(nameof(AdditionalText))]
    private int _additionalCount;

    /// <summary>True when more than one assignment is expiring at once.</summary>
    public bool HasAdditional => AdditionalCount > 0;

    /// <summary>Footnote shown when several assignments expire together.</summary>
    public string AdditionalText =>
        AdditionalCount == 1 ? "+1 more expiring soon" : $"+{AdditionalCount} more expiring soon";

    /// <summary>Copies the live countdown fields off the most-urgent active row.</summary>
    public void UpdateFrom(ActiveAssignmentItemViewModel item, int additionalCount)
    {
        ResourceName = item.DisplayName;
        ContextLabel = $"{item.TenantLabel} · {item.AccountLabel}";
        ScopeLabel = item.Assignment.ScopeLabel;
        RemainingText = item.RemainingText;
        RemainingTime = item.RemainingTime;
        AdditionalCount = additionalCount;
    }
}
