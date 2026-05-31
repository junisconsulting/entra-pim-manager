namespace EntraPimManager.Core.Auth;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

/// <summary>
/// JSON-backed implementation of <see cref="IAccountStore"/>. Reads and writes
/// the enrollment list to <c>accounts.json</c> in the local app data folder.
/// All operations are serialized through an in-process lock so concurrent
/// upserts/removes do not race.
/// </summary>
/// <remarks>
/// The file is not encrypted on disk — it contains metadata only (oid, tenantId,
/// upn, displayName). Tokens stay in the DPAPI-encrypted MSAL cache. If the file
/// is corrupt or unreadable, the store rebuilds it empty and logs a warning.
/// </remarks>
public sealed class AccountStore : IAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger<AccountStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public AccountStore(string filePath, ILogger<AccountStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignedInAccount>> GetAllAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SignedInAccount?> GetByIdAsync(
        string objectId,
        string tenantId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(a => Matches(a, objectId, tenantId));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.ObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.TenantId);

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var accounts = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var existingIndex = accounts.FindIndex(a => Matches(a, account.ObjectId, account.TenantId));

            if (existingIndex >= 0)
            {
                // Preserve the original AddedAt; refresh the mutable identity fields.
                var preserved = accounts[existingIndex];
                accounts[existingIndex] = account with { AddedAt = preserved.AddedAt };
            }
            else
            {
                accounts.Add(account);
            }

            await WriteAsync(accounts, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Account store upserted (oid {ObjectId}, tenant {TenantId})",
                account.ObjectId,
                account.TenantId);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        string objectId,
        string tenantId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var accounts = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var removed = accounts.RemoveAll(a => Matches(a, objectId, tenantId));
            if (removed > 0)
            {
                await WriteAsync(accounts, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Account store removed (oid {ObjectId}, tenant {TenantId})",
                    objectId,
                    tenantId);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReorderAsync(IReadOnlyList<SignedInAccount> orderedAccounts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedAccounts);

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stored = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            if (stored.Count == 0)
            {
                return;
            }

            // Pin each requested key to the matching stored entry (preserving
            // AddedAt and any other server-side fields). Unknown keys are
            // ignored — the caller's view may be stale.
            var pinned = new List<SignedInAccount>(stored.Count);
            var seen = new HashSet<(string Oid, string Tid)>();
            foreach (var requested in orderedAccounts)
            {
                if (requested is null)
                {
                    continue;
                }

                var match = stored.FirstOrDefault(a => Matches(a, requested.ObjectId, requested.TenantId));
                if (match is null)
                {
                    continue;
                }

                pinned.Add(match);
                seen.Add((match.ObjectId.ToLowerInvariant(), match.TenantId.ToLowerInvariant()));
            }

            // Defensive: append stored entries the caller forgot to mention so
            // a stale UI cannot delete enrollments by reordering an incomplete
            // list.
            foreach (var leftover in stored)
            {
                if (!seen.Contains((leftover.ObjectId.ToLowerInvariant(), leftover.TenantId.ToLowerInvariant())))
                {
                    pinned.Add(leftover);
                }
            }

            await WriteAsync(pinned, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Account store reordered ({Count} entries)",
                pinned.Count);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static bool Matches(SignedInAccount account, string objectId, string tenantId)
        => string.Equals(account.ObjectId, objectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(account.TenantId, tenantId, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<SignedInAccount>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var accounts = await JsonSerializer
                .DeserializeAsync<List<SignedInAccount>>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
            return accounts ?? (IReadOnlyList<SignedInAccount>)[];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Account store at {Path} is unreadable; resetting it", _filePath);
            return [];
        }
    }

    private async Task WriteAsync(IReadOnlyList<SignedInAccount> accounts, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file and move atomically so a crash mid-write doesn't
        // leave a half-serialized accounts.json.
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, accounts, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
