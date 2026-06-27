using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Contracts.FireflyApi;
using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Platforms;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Installed;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Services.Update;

public sealed class ModPackUpdateService(
    HttpClient httpClient,
    LauncherConfiguration configuration,
    ILauncherPaths paths,
    IInstalledManifestStore installedManifestStore,
    IDownloader downloader,
    IHashVerifier hashVerifier,
    ModPlatformResolver platformResolver,
    IUpdateTransaction updateTransaction,
    LauncherUserAgent userAgent,
    IDiagnosticLogger logger,
    ConcurrentDownloader concurrentDownloader) : IModPackUpdateService
{
    public Task RecoverAsync(CancellationToken cancellationToken)
    {
        return updateTransaction.RecoverAsync(cancellationToken);
    }

    public async Task<RemoteManifest> ResolveRemoteManifestAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("解析远程整合包清单");
        var modsTask = GetPackModsAsync(cancellationToken);
        var versionTask = GetJsonAsync<VersionInfoResponse>(configuration.FireflyApi.Version, cancellationToken);
        var javaSpecTask = JsonFile.ReadAsync(Path.Combine(AppContext.BaseDirectory, "java-spec.json"), JsonContext.Default.JavaRuntimeSpec, cancellationToken);
        await Task.WhenAll(modsTask, versionTask, javaSpecTask);

        var mods = modsTask.Result ?? [];
        var version = versionTask.Result ?? new VersionInfoResponse(null, null, null, null, null);
        var javaSpec = javaSpecTask.Result ?? new JavaRuntimeSpec("eclipse", 21, "unknown", "jre", "");
        var remoteMods = mods
            .Select(ToRemoteModEntry)
            .Where(static entry => entry is not null)
            .Cast<RemoteModEntry>()
            .ToArray();
        var firefly = new FireflyModEntry(
            version.ModVersion ?? version.Version ?? "unknown",
            version.ModUrl ?? "",
            version.Sha256);
        var packVersion = version.PackVersion ?? version.Version ?? firefly.Version;
        var canonical = JsonSerializer.Serialize(new
        {
            packVersion,
            mods = remoteMods.OrderBy(static m => m.ProjectId, StringComparer.OrdinalIgnoreCase).ThenBy(static m => m.FileName, StringComparer.OrdinalIgnoreCase),
            firefly,
            javaSpec,
            server = configuration.Game.Server
        });
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        logger.LogInformation($"远程清单已解析：{remoteMods.Length} 个 mod，整合包版本 {packVersion}，sha256 {sha256[..12]}");
        return new RemoteManifest(
            1,
            packVersion,
            sha256[..12],
            sha256,
            DateTimeOffset.UtcNow,
            remoteMods,
            firefly,
            javaSpec,
            configuration.Game.Server,
            false);
    }

    public async Task<UpdatePlan> BuildUpdatePlanAsync(RemoteManifest remoteManifest, bool forceVerify, CancellationToken cancellationToken)
    {
        var installed = await installedManifestStore.LoadAsync(cancellationToken);
        var installedMap = installed?.ManagedFiles.ToDictionary(static f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        var downloads = new List<FileToDownload>();
        var keeps = new List<FileToKeep>();
        var resolvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<Exception>();

        foreach (var mod in remoteManifest.Mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var client = platformResolver.Resolve(mod);
                var resolved = await client.ResolveAsync(mod, configuration.Game.MinecraftVersion, "neoforge", cancellationToken);
                var relativePath = $"mods/{resolved.FileName}";
                resolvedPaths.Add(relativePath);
                if (!installedMap.TryGetValue(relativePath, out var local)
                    || forceVerify
                    || !string.Equals(local.Sha1, resolved.Sha1, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(paths.GetAbsoluteGamePath(relativePath)))
                {
                    downloads.Add(new FileToDownload(relativePath, resolved, mod.Required));
                }
                else
                {
                    keeps.Add(new FileToKeep(relativePath, local.Sha1));
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                logger.LogWarning($"解析 mod 失败: {mod.FileName}（{(mod.Required ? "必需" : "可选")}）", ex);
                if (mod.Required)
                {
                    downloads.Add(new FileToDownload(
                        $"mods/{mod.FileName}",
                        new ResolvedModFile(mod, mod.FileName, mod.FileSize, "", new Uri("about:blank")),
                        true));
                }
            }
        }

        var threshold = Math.Max(1, (int)Math.Ceiling(remoteManifest.Mods.Count * configuration.Update.ResolveFailureThresholdPercent / 100d));
        if (failures.Count > threshold)
        {
            logger.LogError($"Mod 解析失败 {failures.Count}/{remoteManifest.Mods.Count}，超过阈值 {threshold}", failures[0]);
            throw new InvalidOperationException($"Mod file resolve failed for {failures.Count}/{remoteManifest.Mods.Count} entries, above threshold {threshold}.", failures[0]);
        }

        var deletes = installedMap.Keys
            .Where(path => path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && !resolvedPaths.Contains(path))
            .Select(static path => new FileToDelete(path))
            .ToArray();

        var fireflyRelativePath = GetFireflyRelativePath(remoteManifest.FireflyMod);
        var fireflyShouldDownload = !string.IsNullOrWhiteSpace(remoteManifest.FireflyMod.DownloadUrl)
            && (installed?.FireflyMod.Version != remoteManifest.FireflyMod.Version
                || !File.Exists(paths.GetAbsoluteGamePath(fireflyRelativePath)));
        var javaChanged = installed is null
            || !string.Equals(installed.JavaRuntimeVersion, remoteManifest.Java.RuntimeVersion, StringComparison.OrdinalIgnoreCase);

        logger.LogDebug($"更新计划：{downloads.Count} 下载，{deletes.Length} 删除，{keeps.Count} 保留，Firefly mod {(fireflyShouldDownload ? "需下载" : "保持")}，Java {(javaChanged ? "变更" : "不变")}");
        return new UpdatePlan(
            remoteManifest.ManifestSha256,
            downloads,
            deletes,
            keeps,
            new FireflyModAction(
                fireflyShouldDownload,
                fireflyShouldDownload ? new Uri(remoteManifest.FireflyMod.DownloadUrl) : null,
                remoteManifest.FireflyMod.Sha256,
                fireflyRelativePath),
            javaChanged);
    }

    public async Task<RemoteManifest> SyncAsync(IProgress<StageProgress>? progress, CancellationToken cancellationToken)
    {
        await RecoverAsync(cancellationToken);
        paths.EnsureCreated();
        progress?.Report(new StageProgress(OperationStage.Resolve, null, 0, "正在解析整合包清单", 0, null, null, null, true));
        var remote = await ResolveRemoteManifestAsync(cancellationToken);
        progress?.Report(new StageProgress(OperationStage.Plan, null, 5, "正在生成更新计划", 0, null, null, null, true));
        var plan = await BuildUpdatePlanAsync(remote, forceVerify: false, cancellationToken);
        logger.LogInformation($"同步整合包：{plan.Downloads.Count} 下载，{plan.Deletes.Count} 删除");
        Directory.CreateDirectory(paths.StagingDirectory);

        var stagedRelativePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stagedSha1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalDownloads = plan.Downloads.Count + (plan.FireflyModAction.ShouldDownload ? 1 : 0);

        // 过滤 about:blank（解析占位），构造并发下载 job；Firefly mod 单独下载不参与并发。
        var modJobs = new List<ConcurrentDownloadJob>();
        foreach (var download in plan.Downloads)
        {
            if (download.Source.DownloadUri.Scheme == "about")
            {
                if (download.Required)
                {
                    logger.LogError($"必需 mod 未能解析，无法继续: {download.Source.Entry.Name}");
                    throw new InvalidOperationException($"Required mod could not be resolved: {download.Source.Entry.Name}");
                }

                logger.LogDebug($"跳过未解析的可选 mod: {download.Source.Entry.Name}");
                continue;
            }

            modJobs.Add(new ConcurrentDownloadJob(
                download.Source.DownloadUri,
                GetStagedPath(download.RelativePath),
                download.RelativePath,
                download.Required,
                string.IsNullOrWhiteSpace(download.Source.Sha1) ? null : download.Source.Sha1,
                RecordActualSha1: true));
        }

        if (modJobs.Count > 0)
        {
            logger.LogInformation($"并发下载 {modJobs.Count} 个 mod（上限 {configuration.Update.MaxConcurrentDownloads}）");
            var modResult = await concurrentDownloader.DownloadAllAsync(
                modJobs,
                configuration.Update.MaxConcurrentDownloads,
                OperationStage.Stage,
                overallBase: 7,
                overallSpan: 70,
                progress,
                cancellationToken);
            foreach (var (relativePath, file) in modResult.Files)
            {
                stagedRelativePaths[relativePath] = file.Path;
                stagedSha1[relativePath] = file.ActualSha1 ?? await hashVerifier.ComputeSha1Async(file.Path, cancellationToken);
            }
        }

        var augmentedDownloads = plan.Downloads.Where(d => stagedRelativePaths.ContainsKey(d.RelativePath)).ToList();
        if (plan.FireflyModAction is { ShouldDownload: true, DownloadUri: not null } firefly)
        {
            var fireflyIndex = modJobs.Count + 1;
            var staged = GetStagedPath(firefly.RelativePath);
            progress?.Report(new StageProgress(OperationStage.Stage, null, 7 + (fireflyIndex * 70d / Math.Max(1, totalDownloads)), firefly.RelativePath, 0, null, null, null, true));
            await downloader.DownloadAsync(firefly.DownloadUri, staged, resume: true, progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(firefly.Sha256)
                && !await hashVerifier.VerifySha256Async(staged, firefly.Sha256, cancellationToken))
            {
                logger.LogError("Firefly mod SHA-256 校验失败");
                throw new InvalidDataException("Firefly mod SHA-256 mismatch.");
            }

            var sha1 = await hashVerifier.ComputeSha1Async(staged, cancellationToken);
            stagedRelativePaths[firefly.RelativePath] = staged;
            stagedSha1[firefly.RelativePath] = sha1;
            var entry = new RemoteModEntry("Firefly Mod", Path.GetFileName(firefly.RelativePath), new FileInfo(staged).Length, ModPlatform.Modrinth, "firefly", remote.FireflyMod.Version, true);
            augmentedDownloads.Add(new FileToDownload(
                firefly.RelativePath,
                new ResolvedModFile(entry, Path.GetFileName(firefly.RelativePath), new FileInfo(staged).Length, sha1, firefly.DownloadUri),
                true));
        }

        var committedPlan = plan with { Downloads = augmentedDownloads };
        progress?.Report(new StageProgress(OperationStage.Commit, null, 92, "正在提交更新事务", 0, null, null, null, false));
        await updateTransaction.ExecuteAsync(
            committedPlan,
            relativePath => stagedRelativePaths[relativePath],
            async () =>
            {
                var managed = committedPlan.Keeps
                    .Select(static keep => new InstalledFile(keep.RelativePath, 0, keep.Sha1))
                    .Concat(committedPlan.Downloads.Select(download =>
                    {
                        var staged = stagedRelativePaths[download.RelativePath];
                        return new InstalledFile(download.RelativePath, new FileInfo(staged).Length, stagedSha1[download.RelativePath]);
                    }))
                    .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await installedManifestStore.SaveAsync(
                    new InstalledManifest(
                        1,
                        remote.PackVersion,
                        remote.ManifestSha256,
                        DateTimeOffset.UtcNow,
                        configuration.Game.MinecraftVersion,
                        configuration.Game.NeoForgeVersion,
                        remote.Java.RuntimeVersion,
                        managed,
                        remote.FireflyMod),
                    cancellationToken);
            },
            progress,
            cancellationToken);
        logger.LogInformation("整合包同步完成");
        return remote;
    }

    private async Task<T?> GetJsonAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(userAgent.Value);
        logger.LogDebug($"GET {uri}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyList<ModEntryResponse>> GetPackModsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, configuration.FireflyApi.PackMods);
        request.Headers.UserAgent.ParseAdd(userAgent.Value);
        logger.LogDebug($"GET {configuration.FireflyApi.PackMods}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ModEntryResponse>>(document.RootElement.GetRawText()) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("mods", out var mods)
            && mods.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ModEntryResponse>>(mods.GetRawText()) ?? [];
        }

        throw new InvalidOperationException("Unexpected /api/pack/mods response shape.");
    }

    private static RemoteModEntry? ToRemoteModEntry(ModEntryResponse response)
    {
        var projectId = response.ProjectId ?? response.PlatformId;
        var fileName = response.FileName ?? response.LegacyFileName ?? response.Name;
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var platform = projectId.All(char.IsDigit)
            ? ModPlatform.CurseForge
            : ModPlatform.Modrinth;
        return new RemoteModEntry(
            response.Name ?? fileName,
            fileName,
            response.FileSize ?? response.Size ?? 0,
            platform,
            projectId,
            string.IsNullOrWhiteSpace(response.Version) ? null : response.Version,
            response.Required ?? true);
    }

    private static string GetFireflyRelativePath(FireflyModEntry firefly)
    {
        if (Uri.TryCreate(firefly.DownloadUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return $"mods/{name}";
            }
        }

        return $"mods/Firefly-{firefly.Version}.jar";
    }

    private string GetStagedPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(paths.StagingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(paths.StagingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Staged path escaped staging directory: {relativePath}");
        }

        return full;
    }
}
