using FireflyMC.Launcher.Models.Remote;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class ModPlatformResolver(ModrinthClient modrinthClient, CurseForgeClient curseForgeClient) : IModPlatformClient
{
    public IModPlatformClient Resolve(RemoteModEntry entry)
    {
        return this;
    }

    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var client in new IModPlatformClient[] { modrinthClient, curseForgeClient })
        {
            try
            {
                return await client.ResolveAsync(entry, minecraftVersion, loader, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex);
            }
        }

        throw new InvalidOperationException(
            $"Unable to resolve {entry.Name} ({entry.ProjectId}) from Modrinth official/mirror or MCIM CurseForge mirror.",
            errors.FirstOrDefault());
    }
}
