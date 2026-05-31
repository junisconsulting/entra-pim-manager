namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Models;
using Microsoft.Graph;

/// <summary>
/// Resolves group ids to <see cref="GroupInfo"/> via batched <c>/groups</c> lookups
/// using the OData <c>id in (...)</c> filter.
/// </summary>
public sealed class GroupResolver : IGroupResolver
{
    private const int ChunkSize = 15;
    private readonly GraphServiceClient _graph;

    public GroupResolver(GraphServiceClient graph)
    {
        _graph = graph;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, GroupInfo>> ResolveAsync(
        IEnumerable<string> groupIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);

        var distinctIds = groupIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase);
        if (distinctIds.Count == 0)
        {
            return result;
        }

        foreach (var chunk in distinctIds.Chunk(ChunkSize))
        {
            var filter = "id in (" + string.Join(",", chunk.Select(id => $"'{id}'")) + ")";

            var response = await _graph.Groups
                .GetAsync(
                    requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = filter;
                        requestConfiguration.QueryParameters.Select = ["id", "displayName", "isAssignableToRole"];
                    },
                    ct)
                .ConfigureAwait(false);

            foreach (var group in response?.Value ?? [])
            {
                if (group.Id is null)
                {
                    continue;
                }

                result[group.Id] = new GroupInfo(
                    Id: group.Id,
                    DisplayName: group.DisplayName ?? group.Id,
                    IsAssignableToRole: group.IsAssignableToRole ?? false);
            }
        }

        return result;
    }
}
