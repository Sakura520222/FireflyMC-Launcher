using FireflyMC.Launcher.Configuration;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public static class McimCachePolicy
{
    public static void EnsureFresh(HttpResponseMessage response, UpdateOptions options)
    {
        if (!response.Headers.TryGetValues("sync_at", out var values))
        {
            return;
        }

        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, out var syncedAt))
        {
            return;
        }

        var maxAge = TimeSpan.FromDays(Math.Max(1, options.McimStaleThresholdDays));
        if (DateTimeOffset.UtcNow - syncedAt.ToUniversalTime() > maxAge)
        {
            throw new InvalidOperationException($"MCIM cache is stale: sync_at={syncedAt:O}.");
        }
    }
}
