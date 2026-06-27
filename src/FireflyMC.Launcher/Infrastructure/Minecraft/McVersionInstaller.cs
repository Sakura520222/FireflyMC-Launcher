using System.IO.Compression;
using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Minecraft;

public sealed class McVersionInstaller(HttpClient httpClient, ILauncherPaths paths, IDownloader downloader, LauncherConfiguration configuration, IDiagnosticLogger logger)
{
    public async Task InstallAsync(string version, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation($"安装 Minecraft {version}");
        paths.EnsureCreated();
        var versionDir = Path.Combine(paths.VersionsDirectory, version);
        Directory.CreateDirectory(versionDir);
        var versionJson = Path.Combine(versionDir, $"{version}.json");
        var clientJar = Path.Combine(versionDir, $"{version}.jar");

        if (!File.Exists(versionJson))
        {
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, 20, "正在下载 Minecraft version.json", 0, null, null, null, true));
            var metadataUri = await ResolveVersionJsonUriAsync(version, cancellationToken);
            await downloader.DownloadAsync(metadataUri, versionJson, resume: true, progress, cancellationToken);
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(versionJson, cancellationToken));
        if (!File.Exists(clientJar)
            && document.RootElement.TryGetProperty("downloads", out var downloads)
            && downloads.TryGetProperty("client", out var client)
            && TryGetString(client, "url") is { } clientUrl)
        {
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, 28, "正在下载 Minecraft client.jar", 0, null, null, null, true));
            await downloader.DownloadAsync(new Uri(clientUrl), clientJar, resume: true, progress, cancellationToken);
        }

        await DownloadLibrariesAsync(document.RootElement, progress, cancellationToken);
        await DownloadAssetsAsync(document.RootElement, progress, cancellationToken);
        await DownloadLoggingAsync(document.RootElement, versionDir, progress, cancellationToken);
        logger.LogInformation($"Minecraft {version} 安装完成");
    }

    private async Task<Uri> ResolveVersionJsonUriAsync(string version, CancellationToken cancellationToken)
    {
        var manifestUri = $"{configuration.Mirrors.MinecraftPrimary}/mc/game/version_manifest_v2.json";
        logger.LogDebug($"GET {manifestUri}");
        using var response = await httpClient.GetAsync(manifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var item in document.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (TryGetString(item, "id") == version && TryGetString(item, "url") is { } url)
            {
                return new Uri(url);
            }
        }

        logger.LogError($"在版本清单中未找到 Minecraft {version}");
        throw new InvalidOperationException($"Minecraft version {version} not found in manifest.");
    }

    private async Task DownloadLibrariesAsync(JsonElement root, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("libraries", out var libraries))
        {
            return;
        }

        var nativeJars = new List<string>();
        foreach (var library in libraries.EnumerateArray())
        {
            if (!AllowsCurrentOs(library))
            {
                continue;
            }

            if (library.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("artifact", out var artifact))
            {
                await DownloadLibraryArtifactAsync(artifact, progress, cancellationToken);
            }

            if (TryGetWindowsNativeClassifier(library) is { } classifier
                && downloads.TryGetProperty("classifiers", out var classifiers)
                && classifiers.TryGetProperty(classifier, out var native))
            {
                var path = await DownloadLibraryArtifactAsync(native, progress, cancellationToken);
                nativeJars.Add(path);
            }
        }

        logger.LogDebug($"下载 {nativeJars.Count} 个 native 库");
        var nativesDir = Path.Combine(paths.VersionsDirectory, "natives");
        Directory.CreateDirectory(nativesDir);
        foreach (var nativeJar in nativeJars)
        {
            ExtractNatives(nativeJar, nativesDir);
        }
    }

    private async Task<string> DownloadLibraryArtifactAsync(JsonElement artifact, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        var relative = TryGetString(artifact, "path") ?? throw new InvalidOperationException("Library artifact is missing path.");
        var url = TryGetString(artifact, "url") ?? throw new InvalidOperationException("Library artifact is missing URL.");
        var target = Path.Combine(paths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, null, relative, 0, null, null, null, true));
            await downloader.DownloadAsync(new Uri(url), target, resume: true, progress, cancellationToken);
        }

        return target;
    }

    private async Task DownloadAssetsAsync(JsonElement root, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("assetIndex", out var assetIndex)
            || TryGetString(assetIndex, "url") is not { } indexUrl
            || TryGetString(assetIndex, "id") is not { } assetId)
        {
            return;
        }

        var indexesDir = Path.Combine(paths.AssetsDirectory, "indexes");
        Directory.CreateDirectory(indexesDir);
        var indexPath = Path.Combine(indexesDir, $"{assetId}.json");
        if (!File.Exists(indexPath))
        {
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, 45, "正在下载资源索引", 0, null, null, null, true));
            await downloader.DownloadAsync(new Uri(indexUrl), indexPath, resume: true, progress, cancellationToken);
        }

        using var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken));
        if (!indexDocument.RootElement.TryGetProperty("objects", out var objects))
        {
            return;
        }

        var assetCount = 0;
        foreach (var property in objects.EnumerateObject())
        {
            if (!property.Value.TryGetProperty("hash", out var hashElement) || hashElement.GetString() is not { } hash)
            {
                continue;
            }

            var prefix = hash[..2];
            var target = Path.Combine(paths.AssetsDirectory, "objects", prefix, hash);
            if (File.Exists(target))
            {
                continue;
            }

            var url = $"https://resources.download.minecraft.net/{prefix}/{hash}";
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, null, property.Name, 0, null, null, null, true));
            await downloader.DownloadAsync(new Uri(url), target, resume: true, progress, cancellationToken);
            assetCount++;
        }

        if (assetCount > 0)
        {
            logger.LogDebug($"下载 {assetCount} 个资源文件");
        }
    }

    private async Task DownloadLoggingAsync(JsonElement root, string versionDir, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("logging", out var logging)
            || !logging.TryGetProperty("client", out var client)
            || !client.TryGetProperty("file", out var file)
            || TryGetString(file, "url") is not { } url
            || TryGetString(file, "id") is not { } id)
        {
            return;
        }

        var target = Path.Combine(versionDir, id);
        if (!File.Exists(target))
        {
            progress?.Report(new StageProgress(OperationStage.Minecraft, null, null, "正在下载日志配置", 0, null, null, null, true));
            await downloader.DownloadAsync(new Uri(url), target, resume: true, progress, cancellationToken);
        }
    }

    private static bool AllowsCurrentOs(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = !rule.TryGetProperty("os", out var os)
                || (TryGetString(os, "name")?.Equals("windows", StringComparison.OrdinalIgnoreCase) == true);
            if (!matches)
            {
                continue;
            }

            allowed = TryGetString(rule, "action") == "allow";
        }

        return allowed;
    }

    private static string? TryGetWindowsNativeClassifier(JsonElement library)
    {
        if (!library.TryGetProperty("natives", out var natives)
            || TryGetString(natives, "windows") is not { } classifier)
        {
            return null;
        }

        return classifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
    }

    private static void ExtractNatives(string jar, string destination)
    {
        using var archive = ZipFile.OpenRead(jar);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
