using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public interface IXboxLiveClient
{
    Task<XboxLiveTokenResponse> RequestUserTokenAsync(string microsoftAccessToken, CancellationToken cancellationToken);
    Task<XstsTokenResponse> RequestXstsTokenAsync(string xboxLiveToken, CancellationToken cancellationToken);
}
