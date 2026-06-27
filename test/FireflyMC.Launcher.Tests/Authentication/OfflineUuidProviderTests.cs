using FireflyMC.Launcher.Services.Accounts.Offline;

namespace FireflyMC.Launcher.Tests.Authentication;

public sealed class OfflineUuidProviderTests
{
    [Fact]
    public void GetUuidString_MatchesJavaOfflinePlayerAlgorithm()
    {
        var provider = new OfflineUuidProvider();

        var uuid = provider.GetUuidString("Steve");

        Assert.Equal("5627dd98e6be3c21b8a8e92344183641", uuid);
    }
}
