namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;

/// <summary>
/// Resolves group ids to display metadata in batches. Needed because
/// <c>$expand=group</c> on the PIM-for-Groups endpoints is unreliable.
/// </summary>
public interface IGroupResolver
{
    /// <summary>
    /// Resolves the given group ids to <see cref="GroupInfo"/>, keyed by id
    /// (case-insensitive). Ids that cannot be resolved are simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<string, GroupInfo>> ResolveAsync(
        IEnumerable<string> groupIds,
        CancellationToken ct = default);
}
