using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonAccountStore(ILauncherPaths paths) : IAccountStore
{
    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        var accounts = await JsonFile.ReadAsync(paths.AccountsFile, JsonContext.Default.IReadOnlyListAccountProfile, cancellationToken);
        return accounts ?? [];
    }

    public Task SaveAsync(IReadOnlyList<AccountProfile> accounts, CancellationToken cancellationToken)
    {
        return JsonFile.WriteAtomicAsync(paths.AccountsFile, accounts, JsonContext.Default.IReadOnlyListAccountProfile, cancellationToken);
    }
}
