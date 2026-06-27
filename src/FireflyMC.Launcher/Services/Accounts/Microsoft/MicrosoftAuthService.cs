using FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Microsoft;

public sealed class MicrosoftAuthService(
    IMicrosoftOAuthClient oauthClient,
    IXboxLiveClient xboxLiveClient,
    IMinecraftServicesClient minecraftServicesClient) : IMicrosoftAuthService
{
    public async Task<IDeviceCodeLoginSession> StartDeviceCodeLoginAsync(CancellationToken cancellationToken)
    {
        var deviceCode = await oauthClient.RequestDeviceCodeAsync(cancellationToken);
        return new DeviceCodeLoginSession(deviceCode);
    }

    public async Task<(AccountProfile Profile, AccountSession Session, MicrosoftCredential Credential)> CompleteDeviceCodeLoginAsync(
        IDeviceCodeLoginSession session,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken, cancellationToken);
        var msToken = await oauthClient.PollTokenAsync(session.DeviceCode, linkedCts.Token);
        var (accountSession, profile, refreshToken) = await ExchangeMinecraftAsync(msToken.AccessToken, msToken.RefreshToken, linkedCts.Token);
        var credential = new MicrosoftCredential(profile.Id, refreshToken, DateTimeOffset.UtcNow);
        return (profile, accountSession, credential);
    }

    public async Task<(AccountSession Session, MicrosoftCredential Credential)> RefreshAsync(MicrosoftCredential credential, CancellationToken cancellationToken)
    {
        var msToken = await oauthClient.RefreshAsync(credential.RefreshToken, cancellationToken);
        var (session, _, refreshToken) = await ExchangeMinecraftAsync(msToken.AccessToken, msToken.RefreshToken ?? credential.RefreshToken, cancellationToken);
        return (session with { AccountId = credential.AccountId }, credential with { RefreshToken = refreshToken, UpdatedAt = DateTimeOffset.UtcNow });
    }

    private async Task<(AccountSession Session, AccountProfile Profile, string RefreshToken)> ExchangeMinecraftAsync(
        string microsoftAccessToken,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Microsoft refresh token is missing.");
        }

        var xbl = await xboxLiveClient.RequestUserTokenAsync(microsoftAccessToken, cancellationToken);
        var uhs = xbl.DisplayClaims.Xui.FirstOrDefault()?.Uhs
            ?? throw new InvalidOperationException("Xbox Live response did not include user hash.");
        var xsts = await xboxLiveClient.RequestXstsTokenAsync(xbl.Token, cancellationToken);
        var xstsUhs = xsts.DisplayClaims.Xui.FirstOrDefault()?.Uhs ?? uhs;
        var mc = await minecraftServicesClient.LoginWithXboxAsync(xstsUhs, xsts.Token, cancellationToken);
        var entitlements = await minecraftServicesClient.GetEntitlementsAsync(mc.AccessToken, cancellationToken);
        if (!entitlements.Items.Any(static item => item.Name.Contains("minecraft", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("game", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该 Microsoft 账号未拥有 Minecraft Java 版。");
        }

        var profile = await minecraftServicesClient.GetProfileAsync(mc.AccessToken, cancellationToken);
        var accountId = $"ms:{profile.Id}";
        var accountProfile = new AccountProfile(accountId, AccountType.Microsoft, profile.Name, profile.Id, DateTimeOffset.UtcNow);
        var session = new AccountSession(
            accountId,
            microsoftAccessToken,
            DateTimeOffset.UtcNow.AddMinutes(55),
            mc.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(mc.ExpiresIn));
        return (session, accountProfile, refreshToken);
    }
}
