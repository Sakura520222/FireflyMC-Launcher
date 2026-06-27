namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class VersionJsonMerger
{
    public string GetPreferredVersionId(string minecraftVersion, string neoForgeVersion)
    {
        return $"neoforge-{neoForgeVersion}";
    }
}
