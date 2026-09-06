namespace EntraPimManager.AppAvalonia.ViewModels;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// View model for the "Add account" slide-in. Mirrors the activation panel
/// pattern: an animated overlay with a "Sign in with" picker over the configured
/// App Registrations (one per tenant), a Sign in button that triggers WAM with
/// <see cref="IAuthService.AddAccountAsync"/>, and a Cancel button.
/// </summary>
/// <remarks>
/// On success, fires <see cref="Closed"/> with the new <see cref="SignedInAccount"/>;
/// on cancel/error, fires it with <c>null</c>. The shell view model listens and
/// updates the account list + active context accordingly.
/// </remarks>
public sealed partial class AddTenantPanelViewModel : ObservableObject
{
    /// <summary>
    /// Shown when the panel is opened without a single App Registration configured.
    /// Reachable via Settings → ACCOUNTS → "Add account…", which is not gated on the
    /// shell's <c>NeedsConfiguration</c> state.
    /// </summary>
    private const string NoRegistrationMessage =
        "No App Registration is configured yet. Add one under Settings → Tenants first.";

    private static readonly TimeSpan AuthCallTimeout = TimeSpan.FromMinutes(2);

    // Device code is completed on a second device (phone), so it needs a much
    // longer budget than the broker flow — the user has to switch devices,
    // browse to the URL, type the code, and sign in (possibly with MFA).
    private static readonly TimeSpan DeviceCodeTimeout = TimeSpan.FromMinutes(10);

    private readonly IAuthService _authService;
    private readonly ILogger<AddTenantPanelViewModel> _logger;

    // Cancellation handle for an in-flight device-code sign-in. Held in a field
    // (rather than a local `using`) so the user can abort the up-to-10-minute
    // poll from the UI. Also carries the 10-minute timeout. Null when idle.
    private CancellationTokenSource? _deviceCodeCts;

    // True when the user explicitly cancelled the device-code flow, so we can
    // tell their abort apart from a 10-minute timeout (both surface as
    // OperationCanceledException) and stay silent instead of showing an error.
    private bool _deviceCodeUserCancelled;

    // True when the caller already named the tenant — the picker then has nothing
    // left to offer and stays hidden.
    private bool _targetPreselected;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Whether the "Advanced" disclosure (which hosts the device-code escape
    /// hatch) is expanded. Collapsed by default — device code is a special case
    /// for tenants whose federated IdP does aggressive seamless SSO.
    /// </summary>
    [ObservableProperty]
    private bool _isAdvancedExpanded;

    /// <summary>
    /// The user code to display during a device-code sign-in, or <c>null</c> when
    /// no device-code flow is in progress. Bound to the instructions panel.
    /// </summary>
    [ObservableProperty]
    private string? _deviceCodeUserCode;

    /// <summary>The verification URL the user must open on a second device.</summary>
    [ObservableProperty]
    private string? _deviceCodeVerificationUri;

    /// <summary>
    /// The App Registration — and thereby the tenant — the user signs in to.
    /// Defaults to the first configured entry.
    /// </summary>
    [ObservableProperty]
    private SignInTarget? _selectedTarget;

    public AddTenantPanelViewModel(
        IAuthService authService,
        IOptions<EntraPimManagerOptions> options,
        ILogger<AddTenantPanelViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _authService = authService;
        _logger = logger;

        // One target per configured entry, in configuration order — the same
        // wording as the cards in Settings → TENANTS. Entries the
        // validator would have rejected cannot occur here; the parse guards are
        // for the compiler, not for a real case.
        var targets = new List<SignInTarget>();
        foreach (var entry in options.Value.TenantAppRegistrations)
        {
            if (!Enum.TryParse<EntraCloud>(entry.Cloud, ignoreCase: true, out var cloud) || !Guid.TryParse(entry.TenantId, out var tenantId))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Label) ? entry.TenantId : entry.Label;
            targets.Add(new SignInTarget(cloud, tenantId.ToString(), $"{name} · {EntraCloudInfo.DisplayName(cloud)}"));
        }

        SignInTargets = targets;
        _selectedTarget = SignInTargets.FirstOrDefault();
    }

    /// <summary>Raised when the panel finishes — payload is null on cancel/error.</summary>
    public event Action<SignedInAccount?>? Closed;

    /// <summary>Options shown in the "Sign in with" ComboBox — one per configured App Registration.</summary>
    public IReadOnlyList<SignInTarget> SignInTargets { get; }

    /// <summary>
    /// Whether the "Sign in with" ComboBox is worth showing. With a single registration,
    /// or when the tenant came from the card the user clicked in, there is nothing to choose.
    /// </summary>
    public bool IsTargetChoiceVisible => SignInTargets.Count > 1 && !_targetPreselected;

    /// <summary>X-offset for the slide-in transform — mirrors <c>ActivationPanelViewModel</c>.</summary>
    public double PanelOffsetX => IsOpen ? 0 : 420;

    /// <summary>True while a device-code sign-in is showing its user code and polling.</summary>
    public bool IsDeviceCodeInProgress => DeviceCodeUserCode is not null;

    /// <summary>
    /// Resets state and opens the slide-in. This is the single "Add account"
    /// surface: the broker sign-in is primary, with the device-code escape hatch
    /// tucked under the Advanced disclosure (collapsed by default).
    /// </summary>
    public void Open()
    {
        _targetPreselected = false;
        Reset();
        SelectedTarget = SignInTargets.FirstOrDefault();
        IsOpen = true;
    }

    /// <summary>
    /// Opens the slide-in for one tenant, chosen in Settings. The picker stays hidden —
    /// the user already said which tenant this is by clicking its card.
    /// </summary>
    /// <param name="cloud">Cloud of the tenant to sign in to.</param>
    /// <param name="tenantId">Tenant to sign in to.</param>
    public void Open(EntraCloud cloud, string tenantId)
    {
        var target = SignInTargets.FirstOrDefault(
            t => t.Cloud == cloud && string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

        // No target means the registration was added but the app has not restarted, so
        // the startup snapshot never saw it. Fall back to the picker rather than opening
        // a panel whose Sign-in button could only fail.
        _targetPreselected = target is not null;
        Reset();
        SelectedTarget = target ?? SignInTargets.FirstOrDefault();
        IsOpen = true;
    }

    private void Reset()
    {
        ErrorMessage = null;
        IsConnecting = false;
        IsAdvancedExpanded = false;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;
        OnPropertyChanged(nameof(IsTargetChoiceVisible));
    }

    partial void OnDeviceCodeUserCodeChanged(string? value)
        => OnPropertyChanged(nameof(IsDeviceCodeInProgress));

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(PanelOffsetX));

    [RelayCommand]
    private void Cancel()
    {
        // Stop any in-flight device-code poll so it doesn't keep running in the
        // background after the panel closes.
        _deviceCodeUserCancelled = true;
        _deviceCodeCts?.Cancel();
        IsOpen = false;
        Closed?.Invoke(null);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedTarget is not { } target)
        {
            ErrorMessage = NoRegistrationMessage;
            return;
        }

        ErrorMessage = null;
        IsConnecting = true;
        try
        {
            using var cts = new CancellationTokenSource(AuthCallTimeout);
            var account = await _authService.AddAccountAsync(target.TenantId, target.Cloud, cts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add account (tenant {TenantId}, cloud {Cloud})", target.TenantId, target.Cloud);
            ErrorMessage = PimErrorMapper.MapException(ex).Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ConnectViaDeviceCodeAsync()
    {
        if (SelectedTarget is not { } target)
        {
            ErrorMessage = NoRegistrationMessage;
            return;
        }

        ErrorMessage = null;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;

        IsConnecting = true;
        _deviceCodeUserCancelled = false;
        _deviceCodeCts = new CancellationTokenSource(DeviceCodeTimeout);
        try
        {
            var account = await _authService.AddAccountViaDeviceCodeAsync(
                target.TenantId,
                target.Cloud,
                challenge =>
                {
                    // MSAL invokes this from a background thread; marshal the
                    // user-facing fields onto the UI thread before they bind.
                    return Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DeviceCodeUserCode = challenge.UserCode;
                        DeviceCodeVerificationUri = challenge.VerificationUri;
                    }).GetTask();
                },
                _deviceCodeCts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (OperationCanceledException)
        {
            // The poll was cancelled. If the user pressed "Cancel sign-in" there's
            // nothing to surface — just drop back to the form. Otherwise the
            // 10-minute device-code window elapsed; tell them so they can retry.
            if (_deviceCodeUserCancelled)
            {
                _logger.LogDebug("Device-code sign-in cancelled by user");
            }
            else
            {
                ErrorMessage = "The device-code sign-in timed out. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device-code sign-in failed for tenant {TenantId} (cloud {Cloud})", target.TenantId, target.Cloud);
            ErrorMessage = PimErrorMapper.MapException(ex).Message;
        }
        finally
        {
            _deviceCodeCts.Dispose();
            _deviceCodeCts = null;
            IsConnecting = false;
            DeviceCodeUserCode = null;
            DeviceCodeVerificationUri = null;
        }
    }

    /// <summary>
    /// Aborts an in-flight device-code sign-in. Bound to the "Cancel sign-in"
    /// button in the instructions panel, which stays enabled while
    /// <see cref="IsConnecting"/> is true (everything else is disabled), so the
    /// user is never stuck waiting out the 10-minute poll.
    /// </summary>
    [RelayCommand]
    private void CancelDeviceCode()
    {
        _deviceCodeUserCancelled = true;
        _deviceCodeCts?.Cancel();
    }

    /// <summary>ComboBox row: one App Registration to sign in with, pinned to its tenant.</summary>
    public sealed record SignInTarget(EntraCloud Cloud, string TenantId, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
