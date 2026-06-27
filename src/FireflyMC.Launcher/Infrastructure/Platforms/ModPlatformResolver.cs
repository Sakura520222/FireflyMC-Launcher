using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class ModPlatformResolver(ModrinthClient modrinthClient, CurseForgeClient curseForgeClient)
{
    public IModPlatformClient Resolve(RemoteModEntry entry)
    {
        return entry.Platform switch
        {
            ModPlatform.Modrinth => modrinthClient,
            ModPlatform.CurseForge => curseForgeClient,
            _ => entry.ProjectId.All(char.IsDigit) ? curseForgeClient : modrinthClient
        };
    }
}
