using FireflyMC.Launcher.Models.Installed;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public interface IInstalledManifestStore
{
    Task<InstalledManifest?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(InstalledManifest manifest, CancellationToken cancellationToken);
}
