namespace FireflyMC.Launcher.Models.Accounts;

public enum AccountType
{
    Microsoft,
    Offline
}

public sealed record AccountProfile(
    string Id,
    AccountType Type,
    string Username,
    string Uuid,
    DateTimeOffset LastUsedAt);

public sealed record MicrosoftCredential(
    string AccountId,
    string RefreshToken,
    DateTimeOffset UpdatedAt);

public sealed record AccountSession(
    string AccountId,
    string? MicrosoftAccessToken,
    DateTimeOffset? MicrosoftAccessTokenExpiresAt,
    string? MinecraftAccessToken,
    DateTimeOffset? MinecraftAccessTokenExpiresAt)
{
    public bool RequiresMinecraftRefresh(TimeSpan skew)
    {
        return string.IsNullOrWhiteSpace(MinecraftAccessToken)
            || MinecraftAccessTokenExpiresAt is null
            || MinecraftAccessTokenExpiresAt <= DateTimeOffset.UtcNow.Add(skew);
    }
}
