using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public interface IMinecraftServicesClient
{
    Task<MinecraftTokenResponse> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken cancellationToken);
    Task<MinecraftEntitlementsResponse> GetEntitlementsAsync(string minecraftAccessToken, CancellationToken cancellationToken);
    Task<MinecraftProfileResponse> GetProfileAsync(string minecraftAccessToken, CancellationToken cancellationToken);
}
