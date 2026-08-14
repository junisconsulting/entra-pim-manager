namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// The complete outcome of one network check run.
/// </summary>
/// <param name="TimestampUtc">When the run started (UTC).</param>
/// <param name="Groups">Group results in catalog order: configured clouds first, update feed last.</param>
public sealed record NetworkDiagnosticsReport(
    DateTimeOffset TimestampUtc,
    IReadOnlyList<NetworkGroupResult> Groups);
