using System.IO.Compression;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Minecraft;

public sealed class AdoptiumJavaRuntimeInstaller(ILauncherPaths paths, IDownloader downloader)
{
    public string JavaExecutable => Path.Combine(paths.JavaRuntimeDirectory, "bin", "java.exe");

    public async Task InstallAsync(JavaRuntimeSpec spec, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        if (File.Exists(JavaExecutable))
        {
            return;
        }

        Directory.CreateDirectory(paths.RuntimeDirectory);
        var archive = Path.Combine(paths.UpdateDirectory, $"java-{spec.RuntimeVersion.Replace('+', '_')}.zip");
        progress?.Report(new StageProgress(OperationStage.Java, null, 8, "正在下载 Java 21", 0, null, null, null, true));
        await downloader.DownloadAsync(new Uri(spec.Url), archive, resume: true, progress, cancellationToken);

        var temp = Path.Combine(paths.RuntimeDirectory, $"java-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        ZipFile.ExtractToDirectory(archive, temp, overwriteFiles: true);
        var root = Directory.EnumerateDirectories(temp).FirstOrDefault() ?? temp;
        if (Directory.Exists(paths.JavaRuntimeDirectory))
        {
            Directory.Delete(paths.JavaRuntimeDirectory, recursive: true);
        }

        Directory.Move(root, paths.JavaRuntimeDirectory);
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
