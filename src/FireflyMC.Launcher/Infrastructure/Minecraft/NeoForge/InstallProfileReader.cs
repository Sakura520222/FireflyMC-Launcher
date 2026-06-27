namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class InstallProfileReader
{
    public string GetInstallerArtifact(string minecraftVersion, string neoForgeVersion)
    {
        return $"net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar";
    }
}
