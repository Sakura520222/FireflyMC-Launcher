using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Models.Installed;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonInstalledManifestStore(ILauncherPaths paths) : IInstalledManifestStore
{
    public Task<InstalledManifest?> LoadAsync(CancellationToken cancellationToken)
    {
        return JsonFile.ReadAsync(paths.InstalledManifestFile, JsonContext.Default.InstalledManifest, cancellationToken);
    }

    public Task SaveAsync(InstalledManifest manifest, CancellationToken cancellationToken)
    {
        return JsonFile.WriteAtomicAsync(paths.InstalledManifestFile, manifest, JsonContext.Default.InstalledManifest, cancellationToken);
    }
}
