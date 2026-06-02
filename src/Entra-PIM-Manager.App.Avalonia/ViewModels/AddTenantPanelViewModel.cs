namespace EntraPimManager.AppAvalonia.ViewModels;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.ErrorHandling;
using Microsoft.Extensions.Logging;

/// <summary>
/// View model for the "Connect to additional tenant" slide-in. Mirrors the
/// activation panel pattern: an animated overlay with one text field for the
/// tenant id / domain, a Connect button that triggers WAM with
/// <see cref="IAuthService.AddAccountForTenantAsync"/>, and a Cancel button.
/// </summary>
/// <remarks>
/// On success, fires <see cref="Closed"/> with the new <see cref="SignedInAccount"/>;
/// on cancel/error, fires it with <c>null</c>. The shell view model listens and
/// updates the account list + active context accordingly.
/// </remarks>
public sealed partial class AddTenantPanelViewModel : ObservableObject
{
    private static readonly TimeSpan AuthCallTimeout = TimeSpan.FromMinutes(2);

    // Device code is completed on a second device (phone), so it needs a much
    // longer budget than the broker flow — the user has to switch devices,
    // browse to the URL, type the code, and sign in (possibly with MFA).
    private static readonly TimeSpan DeviceCodeTimeout = TimeSpan.FromMinutes(10);

    private readonly IAuthService _authService;
    private readonly ILogger<AddTenantPanelViewModel> _logger;

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
    /// Cloud the user has selected for this enrollment. Defaults to
    /// <see cref="EntraCloud.Global"/>; the China option targets the 21Vianet-
    /// operated <c>login.partner.microsoftonline.cn</c> authority and
    /// <c>microsoftgraph.chinacloudapi.cn</c> Graph endpoint.
    /// </summary>
    [ObservableProperty]
    private CloudOption _selectedCloud;

    public AddTenantPanelViewModel(
        IAuthService authService,
        ILogger<AddTenantPanelViewModel> logger)
    {
        _authService = authService;
        _logger = logger;
        _selectedCloud = CloudOptions[0];
    }

    /// <summary>Raised when the panel finishes — payload is null on cancel/error.</summary>
    public event Action<SignedInAccount?>? Closed;

    /// <summary>
    /// Options shown in the cloud ComboBox. Ordered with Global first so the
    /// default keeps the existing single-cloud behaviour.
    /// </summary>
    public IReadOnlyList<CloudOption> CloudOptions { get; } = new[]
    {
        new CloudOption(EntraCloud.Global, EntraCloudInfo.DisplayName(EntraCloud.Global)),
        new CloudOption(EntraCloud.China, EntraCloudInfo.DisplayName(EntraCloud.China)),
    };

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
        SelectedCloud = CloudOptions[0];
        IsAdvancedExpanded = false;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;
        IsOpen = true;
    }

    partial void OnDeviceCodeUserCodeChanged(string? value)
        => OnPropertyChanged(nameof(IsDeviceCodeInProgress));

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(PanelOffsetX));

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Closed?.Invoke(null);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        // Tenant is optional: blank enrolls the identity's home tenant (the
        // common case), a value targets a specific guest/secondary tenant.
        var input = TenantInput?.Trim() ?? string.Empty;
        ErrorMessage = null;

        IsConnecting = true;
        try
        {
            using var cts = new CancellationTokenSource(AuthCallTimeout);
            var account = string.IsNullOrEmpty(input)
                ? await _authService.AddAccountAsync(cts.Token)
                : await _authService.AddAccountForTenantAsync(input, SelectedCloud.Cloud, cts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to add account (tenant {TenantInput}, cloud {Cloud})",
                string.IsNullOrEmpty(input) ? "<home>" : input,
                SelectedCloud.Cloud);
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
        // Tenant input is optional for device code — a blank field enrolls the
        // identity's home tenant, same as the broker "Add account" entry point.
        var input = TenantInput?.Trim();
        ErrorMessage = null;
        DeviceCodeUserCode = null;
        DeviceCodeVerificationUri = null;

        IsConnecting = true;
        try
        {
            using var cts = new CancellationTokenSource(DeviceCodeTimeout);
            var account = await _authService.AddAccountViaDeviceCodeAsync(
                input,
                SelectedCloud.Cloud,
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
                cts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Device-code sign-in failed for tenant {TenantInput} (cloud {Cloud})",
                string.IsNullOrEmpty(input) ? "<home>" : input,
                SelectedCloud.Cloud);
            ErrorMessage = PimErrorMapper.MapException(ex).Message;
        }
        finally
        {
            IsConnecting = false;
            DeviceCodeUserCode = null;
            DeviceCodeVerificationUri = null;
        }
    }

    /// <summary>ComboBox row: pairs the enum value with the localized label.</summary>
    public sealed record CloudOption(EntraCloud Cloud, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
