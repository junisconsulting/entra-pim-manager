namespace EntraPimManager.Core.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using EntraPimManager.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed implementation of <see cref="IJustificationFavoritesStore"/>.
/// Mirrors <see cref="Auth.AccountStore"/>: in-process semaphore, atomic
/// write via temp-file + rename, graceful reset on a corrupt file.
/// </summary>
/// <remarks>
/// The file is not encrypted on disk — it sits in the user's
/// <c>%LocalAppData%</c> alongside <c>accounts.json</c>, which is per-user on
/// Windows. Justification text can carry sensitive incident references; the
/// store never logs the text itself, only ids and role keys.
/// </remarks>
public sealed class JustificationFavoritesStore : IJustificationFavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<JustificationFavoritesStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public JustificationFavoritesStore(string filePath, ILogger<JustificationFavoritesStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JustificationFavorite>> GetForRoleAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(scopeId);

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            return all
                .Where(f => Matches(f, tenantId, kind, resourceId, scopeId))
                .OrderBy(f => f.CreatedAt)
                .ToList();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<JustificationFavorite> AddAsync(
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId,
        string text,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var trimmed = text.Trim();

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = (await ReadAsync(ct).ConfigureAwait(false)).ToList();

            // Idempotent: if the exact text already exists for this role,
            // return the existing entry rather than creating a duplicate. The
            // user clicking Save twice in a row is a common accident.
            var existing = all.FirstOrDefault(f =>
                Matches(f, tenantId, kind, resourceId, scopeId)
                && string.Equals(f.Text, trimmed, StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing;
            }

            var fresh = new JustificationFavorite(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                Kind: kind,
                ResourceId: resourceId,
                ScopeId: scopeId,
                Text: trimmed,
                CreatedAt: DateTimeOffset.UtcNow);
            all.Add(fresh);
            await WriteAsync(all, ct).ConfigureAwait(false);

            // Intentionally NOT logging Text — incident references may be sensitive.
            _logger.LogInformation(
                "Justification favourite added (id {Id}, kind {Kind}, tenant {TenantId})",
                fresh.Id,
                kind,
                tenantId);
            return fresh;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var removed = all.RemoveAll(f => f.Id == id);
            if (removed > 0)
            {
                await WriteAsync(all, ct).ConfigureAwait(false);
                _logger.LogInformation("Justification favourite removed (id {Id})", id);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static bool Matches(
        JustificationFavorite favorite,
        string tenantId,
        PimResourceKind kind,
        string resourceId,
        string scopeId)
        => favorite.Kind == kind
            && string.Equals(favorite.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(favorite.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(favorite.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<JustificationFavorite>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var favorites = await JsonSerializer
                .DeserializeAsync<List<JustificationFavorite>>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            return favorites ?? (IReadOnlyList<JustificationFavorite>)[];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Justification favourites file at {Path} is unreadable; resetting it",
                _filePath);
            return [];
        }
    }

    private async Task WriteAsync(IReadOnlyList<JustificationFavorite> favorites, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Atomic temp-file write — crash mid-serialise leaves the previous
        // favourites.json untouched.
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, favorites, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
