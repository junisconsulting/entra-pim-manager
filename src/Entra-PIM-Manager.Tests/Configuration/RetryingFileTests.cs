namespace EntraPimManager.Tests.Configuration;

using EntraPimManager.Core.Configuration;
using EntraPimManager.Tests.TestSupport;

/// <summary>
/// The stores' reads wait out a scanner's short lock instead of failing at once, and still
/// fail when the lock lasts: a read that never gives up would hang the startup.
/// </summary>
public sealed class RetryingFileTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), "Entra PIM Manager-RetryingFile-" + Guid.NewGuid() + ".json");

    public RetryingFileTests() => File.WriteAllText(_filePath, "[]");

    public void Dispose() => File.Delete(_filePath);

    [Fact]
    public async Task OpenReadAsync_WhenTheLockEndsInTime_Succeeds()
    {
        var held = LockedFile.Hold(_filePath);
        var release = Task.Run(async () =>
        {
            await Task.Delay(30);
            await held.DisposeAsync();
        });

        await using (var stream = await RetryingFile.OpenReadAsync(_filePath, CancellationToken.None))
        {
            Assert.True(stream.CanRead);
        }

        await release;
    }

    [Fact]
    public async Task OpenReadAsync_WhenTheLockOutlastsTheRetries_Throws()
    {
        using (LockedFile.Hold(_filePath))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => RetryingFile.OpenReadAsync(_filePath, CancellationToken.None));
        }
    }
}
