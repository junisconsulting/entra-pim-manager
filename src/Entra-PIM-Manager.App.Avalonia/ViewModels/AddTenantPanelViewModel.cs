namespace EntraPimManager.AppAvalonia.ViewModels;

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

    /// <summary>Resets state and opens the slide-in.</summary>
    public void Open()
    {
        TenantInput = string.Empty;
        ErrorMessage = null;
        IsConnecting = false;
        SelectedCloud = CloudOptions[0];
        IsOpen = true;
    }

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
        var input = TenantInput?.Trim() ?? string.Empty;
        ErrorMessage = null;

        if (string.IsNullOrEmpty(input))
        {
            ErrorMessage = "Please enter a tenant id or domain.";
            return;
        }

        IsConnecting = true;
        try
        {
            using var cts = new CancellationTokenSource(AuthCallTimeout);
            var account = await _authService.AddAccountForTenantAsync(input, SelectedCloud.Cloud, cts.Token);

            IsOpen = false;
            Closed?.Invoke(account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to tenant {TenantInput} (cloud {Cloud})", input, SelectedCloud.Cloud);
            ErrorMessage = PimErrorMapper.MapException(ex).Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>ComboBox row: pairs the enum value with the localized label.</summary>
    public sealed record CloudOption(EntraCloud Cloud, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
