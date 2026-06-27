using System.Net;
using System.Text.Json;
using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public sealed class XboxLiveClient(HttpClient httpClient) : IXboxLiveClient
{
    public async Task<XboxLiveTokenResponse> RequestUserTokenAsync(string microsoftAccessToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={microsoftAccessToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        return await PostAsync<XboxLiveTokenResponse>("https://user.auth.xboxlive.com/user/authenticate", body, cancellationToken);
    }

    public async Task<XstsTokenResponse> RequestXstsTokenAsync(string xboxLiveToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxLiveToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        try
        {
            return await PostAsync<XstsTokenResponse>("https://xsts.auth.xboxlive.com/xsts/authorize", body, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Xbox Live/XSTS 验证失败：账号可能未注册 Xbox、年龄受限或地区不支持。", ex);
        }
    }

    private async Task<T> PostAsync<T>(string uri, object body, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Empty response from {uri}.");
    }
}
