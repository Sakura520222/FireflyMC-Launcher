using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonSettingsStore(ILauncherPaths paths) : ISettingsStore
{
    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await JsonFile.ReadAsync(paths.SettingsFile, JsonContext.Default.LauncherSettings, cancellationToken);
        return settings ?? new LauncherSettings();
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        return JsonFile.WriteAtomicAsync(paths.SettingsFile, settings, JsonContext.Default.LauncherSettings, cancellationToken);
    }
}
