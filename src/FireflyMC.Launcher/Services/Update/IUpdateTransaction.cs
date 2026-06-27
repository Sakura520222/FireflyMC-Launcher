using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Services.Update;

public interface IUpdateTransaction
{
    Task RecoverAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(
        UpdatePlan plan,
        Func<string, string> stagePathSelector,
        Func<Task> saveInstalledManifest,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken);
}
