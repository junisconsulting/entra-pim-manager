namespace EntraPimManager.AppAvalonia.Services;

using Velopack;

/// <summary>
/// A pending update discovered by <see cref="IUpdateService"/>. Wraps the
/// Velopack <see cref="UpdateInfo"/> together with its human-readable version so
/// the controller and view model never take a direct Velopack dependency.
/// </summary>
/// <param name="Version">The target version, e.g. <c>"0.3.0"</c>.</param>
/// <param name="Info">The underlying Velopack update descriptor.</param>
public sealed record UpdateCheckResult(string Version, UpdateInfo Info);
