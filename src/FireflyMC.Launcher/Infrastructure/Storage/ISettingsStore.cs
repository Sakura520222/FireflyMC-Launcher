using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public interface ISettingsStore
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken);
}
