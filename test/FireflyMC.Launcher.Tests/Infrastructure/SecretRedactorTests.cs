using FireflyMC.Launcher.Infrastructure.Crypto;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Infrastructure;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesTokensAndIpAddresses()
    {
        var text = "Authorization: Bearer abc access_token=secret --accessToken token 192.168.1.12";

        var redacted = SecretRedactor.Redact(text);

        redacted.Should().NotContain("secret");
        redacted.Should().NotContain("token 192");
        redacted.Should().NotContain("192.168.1.12");
        redacted.Should().Contain("<redacted>");
        redacted.Should().Contain("<ip>");
    }
}
