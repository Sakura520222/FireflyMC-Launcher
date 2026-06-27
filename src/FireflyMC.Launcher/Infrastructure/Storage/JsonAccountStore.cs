using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonAccountStore(ILauncherPaths paths, IDiagnosticLogger logger) : IAccountStore
{
    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("加载账号列表");
        var accounts = await JsonFile.ReadAsync(paths.AccountsFile, JsonContext.Default.IReadOnlyListAccountProfile, cancellationToken);
        return accounts ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<AccountProfile> accounts, CancellationToken cancellationToken)
    {
        logger.LogInformation($"保存账号列表（{accounts.Count} 个）");
        await JsonFile.WriteAtomicAsync(paths.AccountsFile, accounts, JsonContext.Default.IReadOnlyListAccountProfile, cancellationToken);
    }
}
