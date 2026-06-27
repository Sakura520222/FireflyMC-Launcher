using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Offline;

public interface IOfflineAccountService
{
    AccountProfile Create(string username);
}
