using System.Collections.Concurrent;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Services.Accounts.Microsoft;
using FireflyMC.Launcher.Services.Accounts.Offline;

namespace FireflyMC.Launcher.Services.Accounts;

public sealed class AccountService(
    IAccountStore accountStore,
    ISecretStore secretStore,
    IOfflineAccountService offlineAccountService,
    IMicrosoftAuthService microsoftAuthService) : IAccountService
{
    private readonly ConcurrentDictionary<string, AccountSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        return accountStore.LoadAsync(cancellationToken);
    }

    public Task<IDeviceCodeLoginSession> StartMicrosoftLoginAsync(CancellationToken cancellationToken)
    {
        return microsoftAuthService.StartDeviceCodeLoginAsync(cancellationToken);
    }

    public async Task<AccountProfile> CompleteMicrosoftLoginAsync(IDeviceCodeLoginSession session, CancellationToken cancellationToken)
    {
        var (profile, accountSession, credential) = await microsoftAuthService.CompleteDeviceCodeLoginAsync(session, cancellationToken);
        await secretStore.SaveMicrosoftCredentialAsync(credential, cancellationToken);
        var accounts = (await accountStore.LoadAsync(cancellationToken))
            .Where(account => !string.Equals(account.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
            .Append(profile)
            .OrderByDescending(static account => account.LastUsedAt)
            .ToArray();
        await accountStore.SaveAsync(accounts, cancellationToken);
        _sessions[profile.Id] = accountSession;
        return profile;
    }

    public async Task<AccountProfile> AddOfflineAsync(string username, CancellationToken cancellationToken)
    {
        var profile = offlineAccountService.Create(username);
        var accounts = (await accountStore.LoadAsync(cancellationToken))
            .Where(account => !string.Equals(account.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
            .Append(profile)
            .OrderByDescending(static account => account.LastUsedAt)
            .ToArray();
        await accountStore.SaveAsync(accounts, cancellationToken);
        return profile;
    }

    public async Task LogoutAsync(string accountId, CancellationToken cancellationToken)
    {
        _sessions.TryRemove(accountId, out _);
        var accounts = (await accountStore.LoadAsync(cancellationToken))
            .Where(account => !string.Equals(account.Id, accountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await accountStore.SaveAsync(accounts, cancellationToken);
        await secretStore.DeleteMicrosoftCredentialAsync(accountId, cancellationToken);
    }

    public async Task<AccountSession?> GetOrRefreshSessionAsync(AccountProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Type == AccountType.Offline)
        {
            return new AccountSession(profile.Id, null, null, null, null);
        }

        if (_sessions.TryGetValue(profile.Id, out var cached) && !cached.RequiresMinecraftRefresh(TimeSpan.FromMinutes(5)))
        {
            return cached;
        }

        var semaphore = _refreshLocks.GetOrAdd(profile.Id, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(profile.Id, out cached) && !cached.RequiresMinecraftRefresh(TimeSpan.FromMinutes(5)))
            {
                return cached;
            }

            var credential = await secretStore.LoadMicrosoftCredentialAsync(profile.Id, cancellationToken);
            if (credential is null)
            {
                return null;
            }

            var (session, updatedCredential) = await microsoftAuthService.RefreshAsync(credential, cancellationToken);
            await secretStore.SaveMicrosoftCredentialAsync(updatedCredential, cancellationToken);
            _sessions[profile.Id] = session;
            return session;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
