using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Services.Install;

public interface IInstallService
{
    Task InstallAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken);
    Task RepairAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken);
}
