using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public interface IModPlatformClient
{
    Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken);
}
