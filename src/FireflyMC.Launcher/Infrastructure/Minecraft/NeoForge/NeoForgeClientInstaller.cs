using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class NeoForgeClientInstaller(
    ILauncherPaths paths,
    IDownloader downloader,
    MavenArtifactResolver artifactResolver,
    ProcessorRunner processorRunner,
    IDiagnosticLogger logger)
{
    public async Task InstallAsync(
        string javaExecutable,
        string minecraftVersion,
        string neoForgeVersion,
        bool useMirror,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken)
    {
        var expectedVersionDir = Path.Combine(paths.VersionsDirectory, $"neoforge-{neoForgeVersion}");
        if (Directory.Exists(expectedVersionDir) && Directory.EnumerateFiles(expectedVersionDir, "*.json").Any())
        {
            logger.LogDebug($"NeoForge {neoForgeVersion} 已安装，跳过");
            return;
        }

        logger.LogInformation($"安装 NeoForge {neoForgeVersion}（MC {minecraftVersion}，{(useMirror ? "镜像" : "官方源")}）");
        Directory.CreateDirectory(paths.UpdateDirectory);
        var installerPath = Path.Combine(paths.UpdateDirectory, $"neoforge-{neoForgeVersion}-installer.jar");
        progress?.Report(new StageProgress(OperationStage.NeoForge, null, 55, "正在下载 NeoForge installer", 0, null, null, null, true));
        await downloader.DownloadAsync(artifactResolver.ResolveNeoForgeInstaller(neoForgeVersion, useMirror), installerPath, resume: true, progress, cancellationToken);
        progress?.Report(new StageProgress(OperationStage.NeoForge, null, 60, "正在安装 NeoForge", 0, null, null, null, false));
        await processorRunner.RunAsync(
            javaExecutable,
            $"-jar \"{installerPath}\" --install-client \"{paths.MinecraftDirectory}\"",
            paths.RootDirectory,
            null,
            cancellationToken);
        logger.LogInformation($"NeoForge {neoForgeVersion} 安装完成");
    }
}
