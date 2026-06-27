using FireflyMC.Launcher.Infrastructure.Crypto;

namespace FireflyMC.Launcher.Tests.Infrastructure;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesTokensAndIpAddresses()
    {
        var text = "Authorization: Bearer abc access_token=secret --accessToken token 192.168.1.12";

        var redacted = SecretRedactor.Redact(text);

        Assert.DoesNotContain("secret", redacted);
        Assert.DoesNotContain("token 192", redacted);
        Assert.DoesNotContain("192.168.1.12", redacted);
        Assert.Contains("<redacted>", redacted);
        Assert.Contains("<ip>", redacted);
    }
}
