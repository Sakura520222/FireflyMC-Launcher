using System.Security.Cryptography;

namespace FireflyMC.Launcher.Infrastructure.Crypto;

public sealed class HashVerifier : IHashVerifier
{
    public Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken)
    {
        return ComputeAsync(path, SHA1.Create(), cancellationToken);
    }

    public Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        return ComputeAsync(path, SHA256.Create(), cancellationToken);
    }

    public async Task<bool> VerifySha1Async(string path, string expectedSha1, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            return true;
        }

        var actual = await ComputeSha1Async(path, cancellationToken);
        return string.Equals(actual, Normalize(expectedSha1), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        var actual = await ComputeSha256Async(path, cancellationToken);
        return string.Equals(actual, Normalize(expectedSha256), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeAsync(string path, HashAlgorithm algorithm, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Normalize(string hash)
    {
        return hash.Replace(" ", "", StringComparison.Ordinal).Trim().ToLowerInvariant();
    }
}
