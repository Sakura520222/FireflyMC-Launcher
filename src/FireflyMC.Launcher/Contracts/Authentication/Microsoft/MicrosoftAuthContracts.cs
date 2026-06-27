using System.Text.Json.Serialization;

namespace FireflyMC.Launcher.Contracts.Authentication.Microsoft;

public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("message")] string? Message);

public sealed record MicrosoftTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType);

public sealed record MicrosoftOAuthError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

public sealed record XboxLiveTokenResponse(
    [property: JsonPropertyName("Token")] string Token,
    [property: JsonPropertyName("DisplayClaims")] XboxDisplayClaims DisplayClaims);

public sealed record XboxDisplayClaims([property: JsonPropertyName("xui")] IReadOnlyList<XboxUserInfo> Xui);

public sealed record XboxUserInfo([property: JsonPropertyName("uhs")] string Uhs);

public sealed record XstsTokenResponse(
    [property: JsonPropertyName("Token")] string Token,
    [property: JsonPropertyName("DisplayClaims")] XboxDisplayClaims DisplayClaims);

public sealed record MinecraftTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record MinecraftEntitlementsResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MinecraftEntitlement> Items);

public sealed record MinecraftEntitlement(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("signature")] string? Signature);

public sealed record MinecraftProfileResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
