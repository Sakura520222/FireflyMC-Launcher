using FireflyMC.Launcher.Configuration;

namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class MavenArtifactResolver(LauncherConfiguration configuration)
{
    public Uri ResolveNeoForgeInstaller(string neoForgeVersion, bool useMirror)
    {
        var path = $"net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar";
        var root = useMirror ? configuration.Mirrors.NeoForgeFallback : configuration.Mirrors.NeoForgePrimary;
        return new Uri($"{root.TrimEnd('/')}/{path}");
    }
}
