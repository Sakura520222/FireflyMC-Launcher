using System.Net.Http.Headers;
using System.Text.Json;
using FireflyMC.Launcher.Contracts.Authentication.Microsoft;
using FireflyMC.Launcher.Infrastructure.Diagnostics;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public sealed class MinecraftServicesClient(HttpClient httpClient, IDiagnosticLogger logger) : IMinecraftServicesClient
{
    public async Task<MinecraftTokenResponse> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken cancellationToken)
    {
        logger.LogDebug("用 Xbox 令牌登录 Minecraft 服务");
        var body = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };
        using var response = await httpClient.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MinecraftTokenResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Minecraft login response is empty.");
    }

    public Task<MinecraftEntitlementsResponse> GetEntitlementsAsync(string minecraftAccessToken, CancellationToken cancellationToken)
    {
        logger.LogDebug("查询 Minecraft 所有权");
        return GetAsync<MinecraftEntitlementsResponse>("https://api.minecraftservices.com/entitlements/mcstore", minecraftAccessToken, cancellationToken);
    }

    public Task<MinecraftProfileResponse> GetProfileAsync(string minecraftAccessToken, CancellationToken cancellationToken)
    {
        logger.LogDebug("查询 Minecraft 档案");
        return GetAsync<MinecraftProfileResponse>("https://api.minecraftservices.com/minecraft/profile", minecraftAccessToken, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string uri, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Empty Minecraft services response from {uri}.");
    }
}
