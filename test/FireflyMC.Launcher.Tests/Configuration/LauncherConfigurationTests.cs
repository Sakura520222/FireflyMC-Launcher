using FireflyMC.Launcher.Configuration;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Configuration;

public sealed class LauncherConfigurationTests
{
    [Fact]
    public void Load_WhenJsonOmitsSections_KeepsDefaultOptionObjects()
    {
        var file = Path.Combine(Path.GetTempPath(), $"firefly-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{}");

        var configuration = LauncherConfiguration.Load(file);

        configuration.MicrosoftAuth.Should().NotBeNull();
        configuration.CurseForge.Should().NotBeNull();
        configuration.SelfUpdate.Should().NotBeNull();
        configuration.Game.Should().NotBeNull();
        configuration.Mirrors.Should().NotBeNull();
        configuration.Update.Should().NotBeNull();
        configuration.FireflyApi.Should().NotBeNull();
        LauncherUserAgent.Create(configuration).Value.Should().StartWith("FireflyMC-Launcher/");
    }
}
