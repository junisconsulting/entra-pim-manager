namespace EntraPimManager.Tests.Auth;

using EntraPimManager.Core.Auth;
using Moq;

/// <summary>
/// What an uninstall signs out of Windows' token broker. A miss here leaves a broker account
/// signed in to an app that is gone.
/// </summary>
public sealed class UninstallSignOutTests
{
    [Fact]
    public async Task SignOutBrokerAccountsAsync_RemovesBrokerAccountsOnly()
    {
        var auth = Auth(
            Account("oid-a", "tenant-1", AuthMethod.Broker, EntraCloud.Global),
            Account("oid-b", "tenant-2", AuthMethod.DeviceCode, EntraCloud.Global),
            Account("oid-c", "tenant-3", AuthMethod.Broker, EntraCloud.China));

        await UninstallSignOut.SignOutBrokerAccountsAsync(auth.Object);

        auth.Verify(a => a.RemoveAccountAsync("oid-a", "tenant-1", EntraCloud.Global, It.IsAny<CancellationToken>()), Times.Once());
        auth.Verify(a => a.RemoveAccountAsync("oid-c", "tenant-3", EntraCloud.China, It.IsAny<CancellationToken>()), Times.Once());
        auth.Verify(a => a.RemoveAccountAsync("oid-b", It.IsAny<string>(), It.IsAny<EntraCloud>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task SignOutBrokerAccountsAsync_AFailingAccount_DoesNotStopTheOthers(Type failure)
    {
        // A broker error, or a timeout inside MSAL that surfaces as a cancellation nobody
        // asked for, must not leave the next account signed in.
        var auth = Auth(
            Account("oid-a", "tenant-1", AuthMethod.Broker, EntraCloud.Global),
            Account("oid-b", "tenant-2", AuthMethod.Broker, EntraCloud.Global));
        auth.Setup(a => a.RemoveAccountAsync("oid-a", "tenant-1", EntraCloud.Global, It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(failure)!);

        await UninstallSignOut.SignOutBrokerAccountsAsync(auth.Object);

        auth.Verify(a => a.RemoveAccountAsync("oid-b", "tenant-2", EntraCloud.Global, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task SignOutBrokerAccountsAsync_BudgetSpent_StopsBeforeTheNextAccount()
    {
        // The uninstall hook cancels once its time is up and deletes the data directory next;
        // a sign-out still starting then would recreate the token cache in it.
        using var budget = new CancellationTokenSource();
        var auth = Auth(
            Account("oid-a", "tenant-1", AuthMethod.Broker, EntraCloud.Global),
            Account("oid-b", "tenant-2", AuthMethod.Broker, EntraCloud.Global));
        auth.Setup(a => a.RemoveAccountAsync("oid-a", "tenant-1", EntraCloud.Global, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                budget.Cancel();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => UninstallSignOut.SignOutBrokerAccountsAsync(auth.Object, budget.Token));

        auth.Verify(a => a.RemoveAccountAsync("oid-b", It.IsAny<string>(), It.IsAny<EntraCloud>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static SignedInAccount Account(string oid, string tenantId, AuthMethod method, EntraCloud cloud)
        => new(oid, tenantId, "user", null, DateTimeOffset.UnixEpoch, cloud, method);

    private static Mock<IAuthService> Auth(params SignedInAccount[] accounts)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.GetAllAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        return auth;
    }
}
