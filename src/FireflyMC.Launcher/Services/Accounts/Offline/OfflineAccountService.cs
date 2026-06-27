using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Offline;

public sealed class OfflineAccountService(OfflineUuidProvider uuidProvider, IDiagnosticLogger logger) : IOfflineAccountService
{
    public AccountProfile Create(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            logger.LogWarning("尝试用空用户名创建离线账号");
            throw new ArgumentException("离线账号名不能为空。", nameof(username));
        }

        var normalized = username.Trim();
        var uuid = uuidProvider.GetUuidString(normalized);
        logger.LogInformation($"创建离线账号: {normalized}");
        return new AccountProfile($"offline:{uuid}", AccountType.Offline, normalized, uuid, DateTimeOffset.UtcNow);
    }
}
