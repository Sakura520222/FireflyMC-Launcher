using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class JsonSettingsStore(ILauncherPaths paths, IDiagnosticLogger logger) : ISettingsStore
{
    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("加载启动器设置");
        var settings = await JsonFile.ReadAsync(paths.SettingsFile, JsonContext.Default.LauncherSettings, cancellationToken);
        return settings ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        logger.LogInformation("保存启动器设置");
        await JsonFile.WriteAtomicAsync(paths.SettingsFile, settings, JsonContext.Default.LauncherSettings, cancellationToken);
    }
}
