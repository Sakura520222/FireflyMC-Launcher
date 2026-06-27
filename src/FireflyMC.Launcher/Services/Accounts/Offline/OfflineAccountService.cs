using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Offline;

public sealed class OfflineAccountService(OfflineUuidProvider uuidProvider) : IOfflineAccountService
{
    public AccountProfile Create(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("离线账号名不能为空。", nameof(username));
        }

        var normalized = username.Trim();
        var uuid = uuidProvider.GetUuidString(normalized);
        return new AccountProfile($"offline:{uuid}", AccountType.Offline, normalized, uuid, DateTimeOffset.UtcNow);
    }
}
