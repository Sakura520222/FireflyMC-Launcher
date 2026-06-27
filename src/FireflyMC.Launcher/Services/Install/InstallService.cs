using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
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
    NeoForgeClientInstaller neoForgeClientInstaller,
    IDiagnosticLogger logger) : IInstallService
{
    public async Task InstallAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation($"开始安装（MC {configuration.Game.MinecraftVersion}，NeoForge {configuration.Game.NeoForgeVersion}）");
        paths.EnsureCreated();
        await updateService.RecoverAsync(cancellationToken);
        var remote = await updateService.ResolveRemoteManifestAsync(cancellationToken);
        logger.LogInformation($"整合包清单已解析：{remote.Mods.Count} 个 mod，整合包版本 {remote.PackVersion}");
        await javaRuntimeInstaller.InstallAsync(remote.Java, progress, cancellationToken);
        await mcVersionInstaller.InstallAsync(configuration.Game.MinecraftVersion, progress, cancellationToken);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var java = string.IsNullOrWhiteSpace(settings.JavaPathOverride)
            ? javaRuntimeInstaller.JavaExecutable
            : settings.JavaPathOverride;
        await neoForgeClientInstaller.InstallAsync(java, configuration.Game.MinecraftVersion, configuration.Game.NeoForgeVersion, settings.UseMirror, progress, cancellationToken);
        await updateService.SyncAsync(progress, cancellationToken);
        logger.LogInformation("安装完成");
    }

    public Task RepairAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("开始修复（复用安装流程）");
        return InstallAsync(progress, cancellationToken);
    }
}
