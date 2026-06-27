using System.IO.Compression;
using System.Text.RegularExpressions;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Minecraft;

public sealed class AdoptiumJavaRuntimeInstaller(ILauncherPaths paths, IDownloader downloader, IDiagnosticLogger logger)
{
    private const int ReplaceMaxAttempts = 8;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly Regex JavaTempDirectoryNameRegex = new("^java-[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

    public string JavaExecutable => Path.Combine(paths.JavaRuntimeDirectory, "bin", "java.exe");

    public async Task InstallAsync(JavaRuntimeSpec spec, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        CleanupStaleTempDirectories();
        if (File.Exists(JavaExecutable))
        {
            logger.LogDebug($"Java {spec.RuntimeVersion} 已安装，跳过");
            return;
        }

        logger.LogInformation($"下载并安装 Java {spec.RuntimeVersion}（{spec.Vendor} {spec.ImageType}）");
        Directory.CreateDirectory(paths.RuntimeDirectory);
        var archive = Path.Combine(paths.UpdateDirectory, $"java-{spec.RuntimeVersion.Replace('+', '_')}.zip");
        progress?.Report(new StageProgress(OperationStage.Java, null, 8, "正在下载 Java 21", 0, null, null, null, true));
        await downloader.DownloadAsync(new Uri(spec.Url), archive, resume: true, progress, cancellationToken);

        var temp = Path.Combine(paths.RuntimeDirectory, $"java-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(archive, temp, overwriteFiles: true);
            var root = Directory.EnumerateDirectories(temp).FirstOrDefault() ?? temp;
            await ReplaceJavaRuntimeAsync(root, paths.JavaRuntimeDirectory, cancellationToken);
            logger.LogInformation($"Java {spec.RuntimeVersion} 安装完成");
        }
        finally
        {
            CleanupTempDirectory(temp);
        }
    }

    /// <summary>
    /// 替换 Java 运行时目录（删旧 + 移入新）。Windows Defender/索引器可能短暂锁定刚解压或旧目录中的 exe/dll，
    /// .NET 在这种情况下可能抛 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>，需有限重试。
    /// </summary>
    private async Task ReplaceJavaRuntimeAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, recursive: true);
                }

                Directory.Move(source, destination);
                return;
            }
            catch (Exception ex) when (IsTransientFileAccessException(ex) && attempt < ReplaceMaxAttempts)
            {
                logger.LogWarning($"Java 目录替换遇到临时文件占用，第 {attempt}/{ReplaceMaxAttempts - 1} 次重试", ex);
                await Task.Delay(ReplaceRetryDelay, cancellationToken);
            }
        }
    }

    private void CleanupStaleTempDirectories()
    {
        if (!Directory.Exists(paths.RuntimeDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(paths.RuntimeDirectory, "java-*"))
        {
            if (string.Equals(directory, paths.JavaRuntimeDirectory, StringComparison.OrdinalIgnoreCase)
                || !JavaTempDirectoryNameRegex.IsMatch(Path.GetFileName(directory)))
            {
                continue;
            }

            CleanupTempDirectory(directory);
        }
    }

    private void CleanupTempDirectory(string temp)
    {
        if (!Directory.Exists(temp))
        {
            return;
        }

        try
        {
            Directory.Delete(temp, recursive: true);
        }
        catch (Exception ex) when (IsTransientFileAccessException(ex))
        {
            // temp 残留只占磁盘空间不影响功能；杀毒/索引器锁时先保留，下次启动/安装前会再次尝试清理。
            logger.LogWarning($"临时目录清理被文件占用阻止，暂时保留: {temp}", ex);
        }
    }

    private static bool IsTransientFileAccessException(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || exception is IOException;
    }
}
