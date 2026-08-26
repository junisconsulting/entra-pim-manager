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
/// View model for the "Connect to additional tenant" slide-in. Mirrors the
/// activation panel pattern: an animated overlay with one text field for the
/// tenant id / domain, a Connect button that triggers WAM with
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
    /// Shown when the panel is opened without a single app registration configured.
    /// Reachable via Settings → ACCOUNTS → "Add account…", which is not gated on the
    /// shell's <c>NeedsConfiguration</c> state.
    /// </summary>
    private const string NoRegistrationMessage =
        "No App Registration is configured yet. Enter a client id under Settings → App Registration first.";

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

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _tenantInput = string.Empty;

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
    /// The App Registration the user signs in with. A cloud-wide target leaves the
    /// tenant to the free-text field (blank = home tenant); a tenant-pinned target
    /// fixes the tenant and hides the field. Defaults to the first target.
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

        // One target per App Registration: the cloud-wide one of every cloud that
        // has one (any tenant in that cloud), then every tenant-pinned one. Only
        // real registrations are offered — a cloud without its own client id would
        // just route the user into an opaque AADSTS700016. ConfiguredClouds() is
        // deliberately not used for the first group: it also counts clouds that
        // only have tenant-pinned registrations.
        var settings = options.Value;
        var targets = new List<SignInTarget>();
        foreach (var cloud in Enum.GetValues<EntraCloud>().Where(c => settings.ClientIdFor(c) is not null))
        {
            targets.Add(new SignInTarget(cloud, null, $"{EntraCloudInfo.DisplayName(cloud)} — any tenant"));
        }

        foreach (var pinned in settings.TenantAppRegistrations)
        {
            if (!Enum.TryParse<EntraCloud>(pinned.Cloud, ignoreCase: true, out var cloud) || !Guid.TryParse(pinned.TenantId, out var tenantId))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(pinned.Label) ? pinned.TenantId : pinned.Label;
            targets.Add(new SignInTarget(cloud, tenantId.ToString(), $"{name} · {EntraCloudInfo.DisplayName(cloud)}"));
        }

        SignInTargets = targets;
        _selectedTarget = SignInTargets.FirstOrDefault();
    }

    /// <summary>Raised when the panel finishes — payload is null on cancel/error.</summary>
    public event Action<SignedInAccount?>? Closed;

    /// <summary>
    /// Options shown in the "Sign in with" ComboBox — cloud-wide registrations in
    /// <see cref="EntraCloud"/> declaration order (Global first), then tenant-pinned
    /// ones in configuration order.
    /// </summary>
    public IReadOnlyList<SignInTarget> SignInTargets { get; }

    /// <summary>
    /// Whether the "Sign in with" ComboBox is worth showing. With a single
    /// registration there is nothing to choose.
    /// </summary>
    public bool IsTargetChoiceVisible => SignInTargets.Count > 1;

    /// <summary>
    /// The free-text tenant field only makes sense for a cloud-wide target; a
    /// tenant-pinned registration can sign in to exactly one tenant.
    /// </summary>
    public bool IsTenantInputVisible => SelectedTarget?.TenantId is null;

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
        TenantInput = string.Empty;
        ErrorMessage = null;
        IsConnecting = false;
        SelectedTarget = SignInTargets.FirstOrDefault();
        IsAdvancedExpanded = false;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;
        IsOpen = true;
    }

    partial void OnDeviceCodeUserCodeChanged(string? value)
        => OnPropertyChanged(nameof(IsDeviceCodeInProgress));

    partial void OnSelectedTargetChanged(SignInTarget? value)
        => OnPropertyChanged(nameof(IsTenantInputVisible));

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

        // A pinned target fixes the tenant. Otherwise the tenant is optional:
        // blank enrolls the identity's home tenant (the common case), a value
        // targets a specific guest/secondary tenant.
        var input = target.TenantId ?? TenantInput?.Trim() ?? string.Empty;
        ErrorMessage = null;

        IsConnecting = true;
        try
        {
            using var cts = new CancellationTokenSource(AuthCallTimeout);
            var account = await _authService.AddAccountAsync(input, target.Cloud, cts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to add account (tenant {TenantInput}, cloud {Cloud})",
                string.IsNullOrEmpty(input) ? "<home>" : input,
                target.Cloud);
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

        // Same target semantics as the broker "Add account" entry point: a pinned
        // target fixes the tenant, otherwise a blank field enrolls the home tenant.
        var input = target.TenantId ?? TenantInput?.Trim();
        ErrorMessage = null;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;

        IsConnecting = true;
        _deviceCodeUserCancelled = false;
        _deviceCodeCts = new CancellationTokenSource(DeviceCodeTimeout);
        try
        {
            var account = await _authService.AddAccountViaDeviceCodeAsync(
                input,
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
            _logger.LogError(
                ex,
                "Device-code sign-in failed for tenant {TenantInput} (cloud {Cloud})",
                string.IsNullOrEmpty(input) ? "<home>" : input,
                target.Cloud);
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

    /// <summary>
    /// ComboBox row: one App Registration to sign in with. <paramref name="TenantId"/>
    /// is null for a cloud-wide registration and the pinned tenant's GUID otherwise.
    /// </summary>
    public sealed record SignInTarget(EntraCloud Cloud, string? TenantId, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
