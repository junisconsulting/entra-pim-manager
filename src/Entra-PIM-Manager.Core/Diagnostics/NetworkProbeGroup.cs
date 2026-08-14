namespace EntraPimManager.Core.Diagnostics;

/// <summary>
/// A set of related probes rendered as one section — one group per configured
/// cloud, plus the optional update-feed group.
/// </summary>
/// <param name="DisplayName">Section heading, e.g. the cloud display name.</param>
/// <param name="IsOptional">
/// <c>true</c> when a failure only degrades a convenience feature (auto-update)
/// rather than sign-in or PIM activation.
/// </param>
/// <param name="Probes">The endpoints probed for this group.</param>
public sealed record NetworkProbeGroup(
    string DisplayName,
    bool IsOptional,
    IReadOnlyList<NetworkProbe> Probes);
