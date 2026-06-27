using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Minecraft;
using FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Services.Update;

namespace FireflyMC.Launcher.Services.Install;

public sealed class InstallService(
    LauncherConfiguration configuration,
    ILauncherPaths paths,
    ISettingsStore settingsStore,
    IModPackUpdateService updateService,
    AdoptiumJavaRuntimeInstaller javaRuntimeInstaller,
    McVersionInstaller mcVersionInstaller,
    NeoForgeClientInstaller neoForgeClientInstaller) : IInstallService
{
    public async Task InstallAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        await updateService.RecoverAsync(cancellationToken);
        var remote = await updateService.ResolveRemoteManifestAsync(cancellationToken);
        await javaRuntimeInstaller.InstallAsync(remote.Java, progress, cancellationToken);
        await mcVersionInstaller.InstallAsync(configuration.Game.MinecraftVersion, progress, cancellationToken);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var java = string.IsNullOrWhiteSpace(settings.JavaPathOverride)
            ? javaRuntimeInstaller.JavaExecutable
            : settings.JavaPathOverride;
        await neoForgeClientInstaller.InstallAsync(java, configuration.Game.MinecraftVersion, configuration.Game.NeoForgeVersion, settings.UseMirror, progress, cancellationToken);
        await updateService.SyncAsync(progress, cancellationToken);
    }

    public Task RepairAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        return InstallAsync(progress, cancellationToken);
    }
}
