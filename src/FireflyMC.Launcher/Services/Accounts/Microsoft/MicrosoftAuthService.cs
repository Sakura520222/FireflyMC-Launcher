using FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Accounts.Microsoft;

public sealed class MicrosoftAuthService(
    IMicrosoftOAuthClient oauthClient,
    IXboxLiveClient xboxLiveClient,
    IMinecraftServicesClient minecraftServicesClient,
    IDiagnosticLogger logger) : IMicrosoftAuthService
{
    public async Task<IDeviceCodeLoginSession> StartDeviceCodeLoginAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("请求 Microsoft 设备码");
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
        logger.LogInformation($"Microsoft 登录全链完成: {profile.Id}");
        return (profile, accountSession, credential);
    }

    public async Task<(AccountSession Session, MicrosoftCredential Credential)> RefreshAsync(MicrosoftCredential credential, CancellationToken cancellationToken)
    {
        logger.LogDebug($"刷新账号 {credential.AccountId} 的令牌链");
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
            logger.LogError("Microsoft refresh token 缺失，无法完成令牌交换");
            throw new InvalidOperationException("Microsoft refresh token is missing.");
        }

        logger.LogDebug("交换 Xbox Live 用户令牌");
        var xbl = await xboxLiveClient.RequestUserTokenAsync(microsoftAccessToken, cancellationToken);
        var uhs = xbl.DisplayClaims.Xui.FirstOrDefault()?.Uhs
            ?? throw new InvalidOperationException("Xbox Live response did not include user hash.");
        logger.LogDebug("交换 XSTS 令牌");
        var xsts = await xboxLiveClient.RequestXstsTokenAsync(xbl.Token, cancellationToken);
        var xstsUhs = xsts.DisplayClaims.Xui.FirstOrDefault()?.Uhs ?? uhs;
        logger.LogDebug("登录 Minecraft 服务");
        var mc = await minecraftServicesClient.LoginWithXboxAsync(xstsUhs, xsts.Token, cancellationToken);
        var entitlements = await minecraftServicesClient.GetEntitlementsAsync(mc.AccessToken, cancellationToken);
        if (!entitlements.Items.Any(static item => item.Name.Contains("minecraft", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("game", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogError("账号未拥有 Minecraft Java 版所有权");
            throw new InvalidOperationException("该 Microsoft 账号未拥有 Minecraft Java 版。");
        }

        logger.LogDebug("读取 Minecraft 档案");
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
