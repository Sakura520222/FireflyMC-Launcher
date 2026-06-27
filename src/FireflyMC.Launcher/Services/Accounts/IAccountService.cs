using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Services.Accounts.Microsoft;

namespace FireflyMC.Launcher.Services.Accounts;

public interface IAccountService
{
    Task<IReadOnlyList<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<IDeviceCodeLoginSession> StartMicrosoftLoginAsync(CancellationToken cancellationToken);
    Task<AccountProfile> CompleteMicrosoftLoginAsync(IDeviceCodeLoginSession session, CancellationToken cancellationToken);
    Task<AccountProfile> AddOfflineAsync(string username, CancellationToken cancellationToken);
    Task LogoutAsync(string accountId, CancellationToken cancellationToken);
    Task<AccountSession?> GetOrRefreshSessionAsync(AccountProfile profile, CancellationToken cancellationToken);
}
