namespace EntraPimManager.Core.Auth;

using System.Diagnostics.CodeAnalysis;
using EntraPimManager.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

/// <summary>
/// Creates and registers the MSAL token cache. On Windows the cache file is
/// DPAPI-encrypted and scoped to the current user at
/// <c>%LocalAppData%\Entra-PIM-Manager\msal.cache</c>. A corrupted cache is detected
/// and rebuilt rather than blocking startup.
/// </summary>
/// <remarks>
/// Excluded from coverage: a thin wrapper around <c>MsalCacheHelper</c> and DPAPI
/// file storage that can only be verified meaningfully against the real OS.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class TokenCacheFactory
{
    /// <summary>Default cache file name — used by the Global cloud PCA.</summary>
    public const string DefaultCacheFileName = "msal.cache";

    private readonly ILogger<TokenCacheFactory> _logger;

    public TokenCacheFactory(ILogger<TokenCacheFactory> logger)
    {
        _logger = logger;
        CacheDirectory = AppPaths.DataDirectory;
    }

    /// <summary>Directory holding the encrypted token cache files (non-roaming).</summary>
    public string CacheDirectory { get; }

    /// <summary>Full path of the default (Global cloud) encrypted token cache file.</summary>
    public string CacheFilePath => Path.Combine(CacheDirectory, DefaultCacheFileName);

    /// <summary>
    /// Attaches a DPAPI-encrypted persistent cache to <paramref name="tokenCache"/>
    /// using <paramref name="cacheFileName"/> (defaults to <see cref="DefaultCacheFileName"/>).
    /// The returned helper must be kept alive for the lifetime of the cache.
    /// </summary>
    /// <remarks>
    /// Each sovereign cloud's PCA needs its own cache file so refresh tokens
    /// from different STS authorities don't collide in one binary blob — pass
    /// e.g. <c>msal-china.cache</c> for the China cloud PCA.
    /// </remarks>
    public async Task<MsalCacheHelper> RegisterAsync(
        ITokenCache tokenCache,
        string cacheFileName = DefaultCacheFileName,
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

    /// <summary>
    /// Deletes a named encrypted cache file if it exists. Defaults to the
    /// Global cloud cache for backwards compatibility with single-cloud callers.
    /// </summary>
    public void DeleteCacheFile(string cacheFileName = DefaultCacheFileName)
    {
        var path = Path.Combine(CacheDirectory, cacheFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
