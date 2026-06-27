namespace FireflyMC.Launcher.Infrastructure.Crypto;

public interface IHashVerifier
{
    Task<string> ComputeSha1Async(string path, CancellationToken cancellationToken);
    Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
    Task<bool> VerifySha1Async(string path, string expectedSha1, CancellationToken cancellationToken);
    Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken cancellationToken);
}
