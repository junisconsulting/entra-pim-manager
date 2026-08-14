namespace EntraPimManager.AppAvalonia.ViewModels;

using EntraPimManager.Core.Diagnostics;

/// <summary>
/// Immutable presentation of one network-check group (a cloud, or the update
/// feed) with its per-endpoint rows.
/// </summary>
public sealed class NetworkCheckGroupViewModel
{
    public NetworkCheckGroupViewModel(NetworkGroupResult result)
    {
        var optional = result.Group.IsOptional ? " (optional)" : string.Empty;
        DisplayName = result.Group.DisplayName + optional;
        ProxyLabel = result.ProxyUri is null ? "no proxy" : $"proxy: {result.ProxyUri}";
        Rows = [.. result.Results.Select(r => new NetworkCheckRowViewModel(r))];
    }

    /// <summary>Section heading, with an "(optional)" suffix for the update feed.</summary>
    public string DisplayName { get; }

    /// <summary>The proxy the app's process would use for this group.</summary>
    public string ProxyLabel { get; }

    /// <summary>Per-endpoint result rows, in catalog order.</summary>
    public IReadOnlyList<NetworkCheckRowViewModel> Rows { get; }
}
