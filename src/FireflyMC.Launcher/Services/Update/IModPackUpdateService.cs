using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Services.Update;

public interface IModPackUpdateService
{
    Task RecoverAsync(CancellationToken cancellationToken);
    Task<RemoteManifest> ResolveRemoteManifestAsync(CancellationToken cancellationToken);
    Task<UpdatePlan> BuildUpdatePlanAsync(RemoteManifest remoteManifest, bool forceVerify, CancellationToken cancellationToken);
    Task<RemoteManifest> SyncAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken);
}
