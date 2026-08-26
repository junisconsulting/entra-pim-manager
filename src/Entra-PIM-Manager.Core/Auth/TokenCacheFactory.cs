namespace EntraPimManager.Core.Auth;

using System.Diagnostics.CodeAnalysis;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

/// <summary>
/// Creates and registers MSAL token caches. On Windows each cache file is
/// DPAPI-encrypted and scoped to the current user under
/// <c>%LocalAppData%\junis\Entra-PIM-Manager\</c>. A corrupted cache is detected
/// and rebuilt rather than blocking startup.
/// </summary>
/// <remarks>
/// Excluded from coverage: a thin wrapper around <c>MsalCacheHelper</c> and DPAPI
/// file storage that can only be verified meaningfully against the real OS.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class TokenCacheFactory
{
    private readonly ILogger<TokenCacheFactory> _logger;

    public TokenCacheFactory(ILogger<TokenCacheFactory> logger)
    {
        _logger = logger;
        CacheDirectory = AppPaths.DataDirectory;
    }

    /// <summary>Directory holding the encrypted token cache files (non-roaming).</summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// Attaches a DPAPI-encrypted persistent cache stored as <paramref name="cacheFileName"/>
    /// to <paramref name="tokenCache"/>. The returned helper must be kept alive for the
    /// lifetime of the cache.
    /// </summary>
    /// <remarks>
    /// Each PCA — one per App Registration — needs its own cache file so accounts
    /// and tokens of different registrations and STS authorities don't collide in
    /// one binary blob: <c>msal-{clientId}.cache</c> for the broker PCA and
    /// <c>msal-devicecode-{clientId}.cache</c> for the broker-less one.
    /// </remarks>
    public async Task<MsalCacheHelper> RegisterAsync(
        ITokenCache tokenCache,
        string cacheFileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFileName);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(CacheDirectory);

        var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, CacheDirectory).Build();

        MsalCacheHelper helper;
        try
        {
            helper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
            helper.VerifyPersistence();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "MSAL token cache {File} could not be opened; rebuilding it",
                cacheFileName);
            DeleteCacheFile(cacheFileName);
            helper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
        }

        helper.RegisterCache(tokenCache);
        return helper;
    }

    /// <summary>Deletes a named encrypted cache file if it exists.</summary>
    public void DeleteCacheFile(string cacheFileName)
    {
        var path = Path.Combine(CacheDirectory, cacheFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
