namespace EntraPimManager.Core.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed implementation of <see cref="IUserSettingsService"/>. Mirrors
/// <see cref="JustificationFavoritesStore"/>: in-process semaphore, atomic
/// write via temp-file + rename, graceful fall-back to
/// <see cref="UserSettings.Default"/> on a missing or corrupt file.
/// </summary>
/// <remarks>
/// The file is not encrypted — it lives in the user's per-user
/// <c>%LocalAppData%</c> directory which is already isolated by Windows. The
/// stored fields are non-sensitive preferences (theme choice, default
/// duration, toast settings); no tokens or PII go through this store.
/// </remarks>
public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<UserSettingsService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private UserSettings _current = UserSettings.Default;

    public UserSettingsService(string filePath, ILogger<UserSettingsService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public event Action<UserSettings>? Changed;

    /// <inheritdoc />
    public UserSettings Current => _current;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _current = await ReadAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(UserSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAsync(settings, ct).ConfigureAwait(false);
            _current = settings;
            _logger.LogInformation(
                "User settings saved (theme {Theme}, durationHours {Duration}, expiryWarn {ExpiryEnabled} @ {ExpiryMin}m)",
                settings.Theme,
                settings.DefaultDurationHours,
                settings.ExpiryWarningEnabled,
                settings.ExpiryWarningMinutes);
        }
        finally
        {
            _ioLock.Release();
        }

        // Fire Changed outside the lock so subscribers that re-enter the
        // service (defensive) can't deadlock against us.
        Changed?.Invoke(settings);
    }

    private async Task<UserSettings> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return UserSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer
                .DeserializeAsync<UserSettings>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            return settings ?? UserSettings.Default;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "User settings file at {Path} is unreadable; falling back to defaults",
                _filePath);
            return UserSettings.Default;
        }
    }

    private async Task WriteAsync(UserSettings settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic temp-file write — crash mid-serialise leaves the previous
        // settings.json untouched.
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
