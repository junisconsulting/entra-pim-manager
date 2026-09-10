namespace EntraPimManager.Core.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed implementation of <see cref="IScopeFavoritesStore"/>. Mirrors
/// <see cref="JustificationFavoritesStore"/>: in-process semaphore, atomic write via
/// temp-file + rename, graceful reset on a corrupt file.
/// </summary>
/// <remarks>
/// The file is not encrypted — it sits in the user's <c>%LocalAppData%</c> alongside
/// <c>accounts.json</c>, which is already per-user on Windows. A saved set names
/// management groups and subscriptions of the tenant, so the store logs ids and
/// counts, never the names.
/// </remarks>
public sealed class ScopeFavoritesStore : IScopeFavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<ScopeFavoritesStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private List<ScopeFavorite> _current = [];

    public ScopeFavoritesStore(string filePath, ILogger<ScopeFavoritesStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<ScopeFavorite> Current => _current;

    /// <inheritdoc />
    public IEnumerable<ScopeFavorite> ForRole(string objectId, string tenantId, string resourceId, string scopeId)
        => _current
            .Where(favorite => favorite.BelongsTo(objectId, tenantId, resourceId, scopeId))
            .OrderBy(favorite => favorite.CreatedAt);

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
    public Task AddAsync(ScopeFavorite favorite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        return MutateAsync(all => all.Add(favorite), ct);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid id, CancellationToken ct = default)
        => MutateAsync(all => all.RemoveAll(favorite => favorite.Id == id), ct);

    /// <inheritdoc />
    public Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken ct = default)
        => MutateAsync(
            all =>
            {
                var index = all.FindIndex(favorite => favorite.Id == id);
                if (index >= 0)
                {
                    all[index] = all[index] with { IsPinned = isPinned };
                }
            },
            ct);

    /// <summary>
    /// Applies <paramref name="change"/> to a copy of the list, persists it, and only
    /// then publishes it — a failed write leaves both the file and
    /// <see cref="Current"/> on the previous state rather than on a value that exists
    /// nowhere. <see cref="Changed"/> fires outside the lock so a subscriber that
    /// re-enters cannot deadlock against it.
    /// </summary>
    private async Task MutateAsync(Action<List<ScopeFavorite>> change, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = _current.ToList();
            change(all);
            await WriteAsync(all, ct).ConfigureAwait(false);
            _current = all;
            _logger.LogInformation("Scope favourites saved ({Count} entries)", all.Count);
        }
        finally
        {
            _ioLock.Release();
        }

        Changed?.Invoke();
    }

    private async Task<List<ScopeFavorite>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var favorites = await JsonSerializer
                .DeserializeAsync<List<ScopeFavorite>>(stream, JsonOptions, ct)
                .ConfigureAwait(false);

            // A null entry would survive deserialization and blow up on first use.
            return favorites?.Where(favorite => favorite is not null).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Scope favourites file at {Path} is unreadable; resetting it",
                _filePath);
            return [];
        }
    }

    private async Task WriteAsync(IReadOnlyList<ScopeFavorite> favorites, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic temp-file write — crash mid-serialise leaves the previous
        // scope-favorites.json untouched.
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, favorites, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
