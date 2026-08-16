namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Backing model for the standalone update-prompt window. Mutated in place by
/// <see cref="Tray.UpdateController"/> as the flow moves through its stages
/// (available → downloading → ready / error); the window binds to it directly.
/// UI text is English per the project conventions.
/// </summary>
public sealed partial class UpdatePromptViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(IsDownloading), nameof(IsReady), nameof(IsError))]
    private UpdateStage _stage = UpdateStage.Available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    private string _newVersion = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    private string _currentVersion = string.Empty;

    /// <summary>Download progress, 0..100. Bound to the progress bar.</summary>
    [ObservableProperty]
    private int _progress;

    /// <summary>User-facing message shown in the error stage.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>The stage of the update flow currently shown in the window.</summary>
    public enum UpdateStage
    {
        Available,
        Downloading,
        Ready,
        Error,
    }

    /// <summary>
    /// Version line, e.g. <c>"Version 0.5.0 → 0.6.0"</c>. Users see this popup out of
    /// context, so it names both versions; falls back to the target version alone when
    /// the running version is unknown (non-Velopack run).
    /// </summary>
    public string VersionText => string.IsNullOrEmpty(CurrentVersion)
        ? $"Version {NewVersion}"
        : $"Version {CurrentVersion} → {NewVersion}";

    /// <summary>True while offering the update before any download has started.</summary>
    public bool IsAvailable => Stage == UpdateStage.Available;

    /// <summary>True while the update is downloading.</summary>
    public bool IsDownloading => Stage == UpdateStage.Downloading;

    /// <summary>True once the update is downloaded and ready to apply.</summary>
    public bool IsReady => Stage == UpdateStage.Ready;

    /// <summary>True when the download failed.</summary>
    public bool IsError => Stage == UpdateStage.Error;
}
