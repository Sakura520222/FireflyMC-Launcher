using FireflyMC.Launcher.Infrastructure.Crypto;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Infrastructure;

public sealed class HashVerifierTests
{
    [Fact]
    public async Task ComputeHashes_ReturnsExpectedVectors()
    {
        var file = Path.Combine(Path.GetTempPath(), $"firefly-hash-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "abc");
        var verifier = new HashVerifier();

        var sha1 = await verifier.ComputeSha1Async(file, CancellationToken.None);
        var sha256 = await verifier.ComputeSha256Async(file, CancellationToken.None);

        sha1.Should().Be("a9993e364706816aba3e25717850c26c9cd0d89d");
        sha256.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}
