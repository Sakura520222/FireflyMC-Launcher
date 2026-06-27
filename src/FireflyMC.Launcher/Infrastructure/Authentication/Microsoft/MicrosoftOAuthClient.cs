using System.Net;
using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;

public sealed class MicrosoftOAuthClient(HttpClient httpClient, MicrosoftAuthOptions options) : IMicrosoftOAuthClient
{
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        EnsureClientConfigured();
        var uri = $"https://login.microsoftonline.com/{options.Tenant}/oauth2/v2.0/devicecode";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["scope"] = string.Join(' ', options.Scopes)
        });
        using var response = await httpClient.PostAsync(uri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<DeviceCodeResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft device code response is empty.");
    }

    public async Task<MicrosoftTokenResponse> PollTokenAsync(DeviceCodeResponse deviceCode, CancellationToken cancellationToken)
    {
        EnsureClientConfigured();
        var interval = Math.Max(1, deviceCode.Interval);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = options.ClientId,
                ["device_code"] = deviceCode.DeviceCode
            });
            using var response = await httpClient.PostAsync($"https://login.microsoftonline.com/{options.Tenant}/oauth2/v2.0/token", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<MicrosoftTokenResponse>(stream, cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("Microsoft token response is empty.");
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            switch (error?.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "authorization_declined":
                    throw new InvalidOperationException("用户拒绝了 Microsoft 登录授权。");
                case "expired_token":
                    throw new TimeoutException("设备代码已过期，请重新获取代码。");
                case "bad_verification_code":
                    throw new InvalidOperationException("设备代码无效，请重新获取代码。");
                default:
                    throw new InvalidOperationException(error?.ErrorDescription ?? $"Microsoft token polling failed: {response.StatusCode}");
            }
        }

        throw new TimeoutException("设备代码已过期，请重新获取代码。");
    }

    public async Task<MicrosoftTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        EnsureClientConfigured();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = string.Join(' ', options.Scopes)
        });
        using var response = await httpClient.PostAsync($"https://login.microsoftonline.com/{options.Tenant}/oauth2/v2.0/token", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MicrosoftTokenResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft refresh response is empty.");
    }

    private void EnsureClientConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException("MicrosoftAuth.ClientId 未配置。请在 appsettings.json 中填入 Microsoft OAuth public client_id。");
        }
    }

    private static async Task<MicrosoftOAuthError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MicrosoftOAuthError>(stream, cancellationToken: cancellationToken);
    }
}
