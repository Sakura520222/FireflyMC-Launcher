using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public interface IMicrosoftOAuthClient
{
    Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken);
    Task<MicrosoftTokenResponse> PollTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken);
    Task<MicrosoftTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
