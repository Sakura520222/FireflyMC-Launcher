using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public interface ISecretStore
{
    Task<MicrosoftCredential?> LoadMicrosoftCredentialAsync(string accountId, CancellationToken cancellationToken);
    Task SaveMicrosoftCredentialAsync(MicrosoftCredential credential, CancellationToken cancellationToken);
    Task DeleteMicrosoftCredentialAsync(string accountId, CancellationToken cancellationToken);
}
