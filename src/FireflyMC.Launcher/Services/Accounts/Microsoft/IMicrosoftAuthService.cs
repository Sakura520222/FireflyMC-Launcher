using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Microsoft;

public interface IMicrosoftAuthService
{
    Task<IDeviceCodeLoginSession> StartDeviceCodeLoginAsync(CancellationToken cancellationToken);
    Task<(AccountProfile Profile, AccountSession Session, MicrosoftCredential Credential)> CompleteDeviceCodeLoginAsync(IDeviceCodeLoginSession session, CancellationToken cancellationToken);
    Task<(AccountSession Session, MicrosoftCredential Credential)> RefreshAsync(MicrosoftCredential credential, CancellationToken cancellationToken);
}
