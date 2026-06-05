namespace EntraPimManager.AppAvalonia.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Shows Windows toast notifications. Justification text is never included.
/// </summary>
public interface IToastService
{
    /// <summary>Shows the outcome of an activation request.</summary>
    void ShowActivationResult(string resourceName, ActivationResult result);

    /// <summary>Shows the outcome of a deactivation request.</summary>
    void ShowDeactivationResult(string resourceName, ActivationResult result);

    /// <summary>Shows a generic error notification.</summary>
    void ShowError(string title, string detail);
}
