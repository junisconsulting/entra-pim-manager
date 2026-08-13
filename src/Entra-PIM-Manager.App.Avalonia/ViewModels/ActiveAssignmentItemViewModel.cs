namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Models;

/// <summary>A single row in the (cross-tenant) active-assignments list, with a live countdown.</summary>
public sealed partial class ActiveAssignmentItemViewModel : ObservableObject
{
    /// <summary>
    /// Microsoft enforces a minimum active duration of 5 minutes before a
    /// PIM role can be self-deactivated. Earlier attempts return HTTP 201
    /// with status=Failed and the portal-side error
    /// <c>"The Active duration is too short. Minimum Required is 5 minutes."</c>
    /// The stop button is hard-disabled during this lockout so users don't
    /// get a failed-toast they can't act on; the tooltip surfaces both the
    /// policy and the remaining wait time.
    /// </summary>
    private static readonly TimeSpan ProvisioningWindow = TimeSpan.FromMinutes(5);

    private readonly Func<ActiveAssignmentItemViewModel, Task> _deactivate;

    [ObservableProperty]
    private string _remainingText = string.Empty;

    /// <summary>
    /// Remaining time on the assignment. Drives the bar/text colour through
    /// <see cref="RemainingTimeToBrushConverter"/>: accent → warning at ≤ 15 min
    /// → danger at ≤ 5 min.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _remainingTime;

    /// <summary>
    /// Fraction of the activation window still ahead (1.0 → 0.0). Drives the
    /// countdown bar spanning the bottom of the row, which drains from full to
    /// empty as the activation elapses.
    /// </summary>
    [ObservableProperty]
    private double _progressFraction;

    /// <summary>
    /// Tenant display name (e.g. <c>"junis"</c>), filled asynchronously by the
    /// shell once <c>/organization</c> resolves. <c>null</c> while in flight.
    /// </summary>
    [ObservableProperty]
    private string? _tenantName;

    /// <summary>
    /// True while we're waiting for Graph to confirm the activation; the row
    /// renders a spinner instead of the countdown and hides the deactivate
    /// button. Reset to false once <see cref="ShellViewModel.UpdateActiveAssignments"/>
    /// sees the real assignment and either replaces or merges this row.
    /// </summary>
    [ObservableProperty]
    private bool _isPending;

    /// <summary>
    /// True while we're waiting for Graph to confirm the deactivation; the row
    /// renders a spinner + "Deactivating…" instead of the countdown and hides
    /// the deactivate button so the user can't double-click. Reset to false
    /// only on failure — on success the row vanishes from the list entirely.
    /// </summary>
    [ObservableProperty]
    private bool _isDeactivating;

    /// <summary>
    /// Inline error caption shown under the role meta when a deactivation
    /// attempt fails after the shell's retry budget is exhausted. Surfaces the
    /// failure inside the row itself so users never lose the signal when
    /// Windows toast notifications are suppressed (Focus Assist, DND). Cleared
    /// at the start of the next deactivation attempt.
    /// </summary>
    [ObservableProperty]
    private string? _deactivationErrorText;

    public ActiveAssignmentItemViewModel(
        ActiveAssignment assignment,
        SignedInAccount account,
        Func<ActiveAssignmentItemViewModel, Task> deactivate)
    {
        Assignment = assignment;
        Account = account;
        _deactivate = deactivate;
        UpdateCountdown();
    }

    /// <summary>The underlying active assignment.</summary>
    public ActiveAssignment Assignment { get; }

    /// <summary>The account this assignment belongs to — needed to route the deactivate call.</summary>
    public SignedInAccount Account { get; }

    /// <summary>Display name of the role or group.</summary>
    public string DisplayName => Assignment.DisplayName;

    /// <summary>
    /// Display name of the identity that activated this role — used in the toast
    /// title and as a context line in the active row. Falls back to UPN when no
    /// display name is set in Entra.
    /// </summary>
    public string AccountLabel => Account.DisplayName ?? Account.Username;

    /// <summary>UPN of the identity that activated this role.</summary>
    public string Username => Account.Username;

    /// <summary>Tenant GUID of the enrollment this activation belongs to.</summary>
    public string TenantId => Account.TenantId;

    /// <summary>
    /// Aggregate transition flag: true while either activation or deactivation
    /// is in flight. The XAML uses this to hide the countdown and the
    /// deactivate button so only one of the two spinner stacks is visible.
    /// </summary>
    public bool IsBusy => IsPending || IsDeactivating;

    /// <summary>
    /// True for the first <see cref="ProvisioningWindow"/> after activation
    /// start. The XAML binds the stop button's <c>IsEnabled</c> to its
    /// inverse so the button is hard-disabled during Microsoft's 5-minute
    /// minimum-active-duration policy, avoiding the failed-toast we'd
    /// otherwise see on every too-early click. Updated each tick by
    /// <see cref="UpdateCountdown"/>.
    /// </summary>
    public bool IsInProvisioningWindow =>
        Assignment.StartDateTime is { } start
        && DateTimeOffset.UtcNow - start < ProvisioningWindow;

    /// <summary>
    /// Deactivate-button tooltip: a generic label outside the lockout window,
    /// the policy explanation + remaining wait time during it.
    /// </summary>
    public string DeactivateTooltip
    {
        get
        {
            if (Assignment.StartDateTime is not { } start)
            {
                return "Deactivate";
            }

            var elapsed = DateTimeOffset.UtcNow - start;
            if (elapsed >= ProvisioningWindow)
            {
                return "Deactivate";
            }

            var remaining = ProvisioningWindow - elapsed;
            var remainingText = remaining.TotalMinutes >= 1
                ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds}s"
                : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s";
            return $"Microsoft requires a minimum active duration of {(int)ProvisioningWindow.TotalMinutes} minutes before a role can be deactivated. Available in {remainingText}.";
        }
    }

    /// <summary>
    /// Composed tenant label: <c>"{TenantName}"</c> when resolved, GUID fallback
    /// otherwise. The active row binds this so the user sees which tenant the
    /// role was activated in (separate from the account name).
    /// </summary>
    public string TenantLabel => TenantLabelFormatter.Format(TenantName, TenantId);

    /// <summary>Localized label for the resource kind.</summary>
    public string KindLabel => Assignment.Kind switch
    {
        PimResourceKind.DirectoryRole => "Directory role",
        PimResourceKind.GroupMembership => "Group membership",
        PimResourceKind.GroupOwnership => "Group ownership",
        _ => string.Empty,
    };

    /// <summary>
    /// Tenant and resource kind as one string for the row's meta line.
    /// </summary>
    /// <remarks>
    /// Composed here rather than laid out as three controls in the view on purpose:
    /// a horizontal <c>StackPanel</c> measures its children against infinite width,
    /// so <c>TextTrimming</c> never engages and a long tenant name (e.g. a CJK one)
    /// overflows its grid column and paints over the countdown. One TextBlock bound
    /// to one string trims correctly at any width.
    /// </remarks>
    public string MetaLine => string.IsNullOrEmpty(KindLabel)
        ? TenantLabel
        : $"{TenantLabel} · {KindLabel}";

    /// <summary>
    /// Wall-clock time this row was constructed. Used by the shell's pending
    /// watchdog: a placeholder that hasn't been replaced by a real assignment
    /// within ~30 s is assumed lost and dropped to avoid stale "Activating…" UI.
    /// </summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a placeholder row shown immediately after a successful activation
    /// while we wait for Graph PIM to surface the assignment on its read API
    /// (typically 5–10 seconds of eventual consistency). The placeholder synthesizes
    /// a fake <see cref="ActiveAssignment"/> from the eligibility + requested
    /// duration so identity/tenant/kind/countdown all bind normally; the row marks
    /// itself <see cref="IsPending"/> so the XAML can swap countdown ↔ spinner
    /// and hide the deactivate button.
    /// </summary>
    public static ActiveAssignmentItemViewModel CreatePending(
        PimEligibility eligibility,
        SignedInAccount account,
        TimeSpan duration,
        Func<ActiveAssignmentItemViewModel, Task> deactivate)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(account);

        var now = DateTimeOffset.UtcNow;
        var placeholder = new ActiveAssignment(
            Kind: eligibility.Kind,
            DisplayName: eligibility.DisplayName,
            ResourceId: eligibility.ResourceId,
            ScopeId: eligibility.ScopeId,
            PrincipalId: eligibility.PrincipalId,
            StartDateTime: now,
            EndDateTime: now + duration,
            AssignmentScheduleId: string.Empty);

        return new ActiveAssignmentItemViewModel(placeholder, account, deactivate)
        {
            IsPending = true,
        };
    }

    /// <summary>
    /// Recomputes the countdown text, the remaining-time field that drives the
    /// bar/text colour, and the 0..1 progress fraction. The expiring-soon
    /// toast threshold lives on <see cref="ShellViewModel"/> (user-configurable).
    /// </summary>
    public void UpdateCountdown()
    {
        if (Assignment.EndDateTime is not { } end)
        {
            RemainingText = "No expiry";
            RemainingTime = TimeSpan.Zero;

            // Full, not empty: nothing has drained. As a 44px arc a zero sweep was
            // merely ambiguous, but the full-width bar would read as "0 % left, about
            // to expire" — the exact opposite of what this branch means.
            ProgressFraction = 1.0;
            return;
        }

        var remaining = end - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            RemainingText = "Expired";
            RemainingTime = TimeSpan.Zero;
            ProgressFraction = 0;
            return;
        }

        RemainingTime = remaining;

        // Bar fill proportional to remaining / total. StartDateTime can be
        // null for activations we read back from Graph before its scheduledStart
        // field is populated; fall back to a full bar in that case so the
        // countdown text alone carries the information.
        if (Assignment.StartDateTime is { } start && end > start)
        {
            var total = end - start;
            ProgressFraction = Math.Clamp(remaining.TotalSeconds / total.TotalSeconds, 0.0, 1.0);
        }
        else
        {
            ProgressFraction = 1.0;
        }

        // Format: terse, no trailing "left" — the row has no width to spare next
        // to the tenant name. Surface seconds in the final minute so the countdown
        // visibly ticks instead of sitting on "0m" for 60 seconds.
        if (remaining.TotalHours >= 1)
        {
            RemainingText = $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        }
        else if (remaining.TotalMinutes >= 1)
        {
            RemainingText = $"{remaining.Minutes}m";
        }
        else
        {
            RemainingText = $"{Math.Max(1, remaining.Seconds)}s";
        }

        // Re-evaluate the provisioning-window-derived properties so the stop
        // button re-enables itself automatically once the lockout expires.
        OnPropertyChanged(nameof(IsInProvisioningWindow));
        OnPropertyChanged(nameof(DeactivateTooltip));
    }

    [RelayCommand]
    private Task DeactivateAsync() => _deactivate(this);

    partial void OnTenantNameChanged(string? value)
    {
        OnPropertyChanged(nameof(TenantLabel));
        OnPropertyChanged(nameof(MetaLine));
    }

    partial void OnIsPendingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnIsDeactivatingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));
}
