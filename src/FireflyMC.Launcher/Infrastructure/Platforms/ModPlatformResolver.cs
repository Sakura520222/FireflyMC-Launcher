using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class ModPlatformResolver(ModrinthClient modrinthClient, CurseForgeClient curseForgeClient, IDiagnosticLogger logger) : IModPlatformClient
{
    public IModPlatformClient Resolve(RemoteModEntry entry)
    {
        return this;
    }

    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        logger.LogDebug($"解析 mod {entry.ProjectId}（{entry.Platform}）：Modrinth → CurseForge MCIM 降级链");
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

        logger.LogWarning($"所有平台均无法解析 {entry.Name}（{entry.ProjectId}）", errors.FirstOrDefault());
        throw new InvalidOperationException(
            $"Unable to resolve {entry.Name} ({entry.ProjectId}) from Modrinth official/mirror or MCIM CurseForge mirror.",
            errors.FirstOrDefault());
    }
}
