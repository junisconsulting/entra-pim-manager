namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="UserSettings"/> to a JSON file in
/// <c>%LocalAppData%</c>. The cached <see cref="Current"/> is loaded once at
/// app startup and refreshed on every <see cref="SaveAsync"/>; subscribers get
/// a <see cref="Changed"/> notification so live UI (theme, expiry threshold,
/// activation defaults) can react without restarting.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Raised after <see cref="SaveAsync"/> finishes writing — fires
    /// synchronously on the caller's thread. Subscribers that touch UI must
    /// marshal to the dispatcher themselves.
    /// </summary>
    event Action<UserSettings>? Changed;

    /// <summary>
    /// The currently active settings. Before <see cref="LoadAsync"/> has run,
    /// this returns <see cref="UserSettings.Default"/>.
    /// </summary>
    UserSettings Current { get; }

    /// <summary>
    /// Reads the settings file from disk. Called once during host startup.
    /// If the file is missing or corrupt, <see cref="Current"/> stays on
    /// <see cref="UserSettings.Default"/> and no exception is thrown.
    /// </summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the new settings atomically (temp-file + rename), updates
    /// <see cref="Current"/>, and fires <see cref="Changed"/>. Saving the very instance
    /// that is already <see cref="Current"/> does nothing.
    /// </summary>
    Task SaveAsync(UserSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Applies <paramref name="transform"/> to the latest settings under the write lock and
    /// persists the result like <see cref="SaveAsync"/>. Two quick updates therefore never
    /// start from the same snapshot, so neither can drop the other's change. A transform
    /// that returns its input unchanged skips the write and <see cref="Changed"/>. The
    /// transform may run on a thread-pool thread: it must not touch UI state.
    /// </summary>
    Task UpdateAsync(Func<UserSettings, UserSettings> transform, CancellationToken ct = default);
}
