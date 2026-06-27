using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Installed;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonInstalledManifestStore(ILauncherPaths paths, IDiagnosticLogger logger) : IInstalledManifestStore
{
    public async Task<InstalledManifest?> LoadAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("加载已安装清单");
        return await JsonFile.ReadAsync(paths.InstalledManifestFile, JsonContext.Default.InstalledManifest, cancellationToken);
    }

    public async Task SaveAsync(InstalledManifest manifest, CancellationToken cancellationToken)
    {
        logger.LogInformation($"保存已安装清单（{manifest.ManagedFiles.Count} 个受管理文件，pack {manifest.LastInstalledPackVersion}）");
        await JsonFile.WriteAtomicAsync(paths.InstalledManifestFile, manifest, JsonContext.Default.InstalledManifest, cancellationToken);
    }
}
