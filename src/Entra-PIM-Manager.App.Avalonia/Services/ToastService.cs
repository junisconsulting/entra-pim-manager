namespace EntraPimManager.AppAvalonia.Services;

using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

/// <summary>
/// Shows toast notifications via <see cref="ToastContentBuilder"/>. A failure to
/// show a toast is logged and swallowed — it must never break an activation flow.
/// Justification text is intentionally never included in the toast body.
/// </summary>
public sealed class ToastService : IToastService
{
    private readonly ILogger<ToastService> _logger;

    public ToastService(ILogger<ToastService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void ShowActivationResult(string resourceName, ActivationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (title, message) = result switch
        {
            { IsSuccess: true, Status: ActivationStatus.PendingApproval } =>
                ("Approval requested", $"{resourceName}: the activation is waiting for approval."),
            { IsSuccess: true } =>
                ("Role activated", $"{resourceName} is now active."),
            { Error: { } error } =>
                ("Activation failed", $"{resourceName}: {error.Message}"),
            _ => ("Activation", resourceName),
        };

        Show(title, message);
    }

    /// <inheritdoc />
    public void ShowDeactivationResult(string resourceName, ActivationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (title, message) = result.Error is { } error
            ? ("Deactivation failed", $"{resourceName}: {error.Message}")
            : ("Role deactivated", $"{resourceName} was deactivated.");

        Show(title, message);
    }

    /// <inheritdoc />
    public void ShowExpiringSoon(string resourceName) =>
        Show("Activation expiring soon", $"{resourceName} expires in less than 5 minutes.");

    /// <inheritdoc />
    public void ShowError(string title, string detail) => Show(title, detail);

    private void Show(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toast notification could not be shown");
        }
    }
}
