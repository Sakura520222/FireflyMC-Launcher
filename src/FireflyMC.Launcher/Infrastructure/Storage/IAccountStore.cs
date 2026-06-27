using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public interface IAccountStore
{
    Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<AccountProfile> accounts, CancellationToken cancellationToken);
}
