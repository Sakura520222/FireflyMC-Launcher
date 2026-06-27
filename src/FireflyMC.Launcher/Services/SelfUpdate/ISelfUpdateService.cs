namespace FireflyMC.Launcher.Services.SelfUpdate;

public interface ISelfUpdateService
{
    Task<LauncherUpdateInfo?> CheckAsync(CancellationToken cancellationToken);
    Task StartUpdateAsync(LauncherUpdateInfo updateInfo, CancellationToken cancellationToken);
}
