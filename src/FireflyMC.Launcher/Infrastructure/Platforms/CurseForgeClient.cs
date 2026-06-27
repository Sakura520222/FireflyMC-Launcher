using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class CurseForgeClient(
    HttpClient httpClient,
    LauncherConfiguration configuration,
    MirrorRouter mirrorRouter,
    LauncherUserAgent userAgent,
    IDiagnosticLogger logger) : IModPlatformClient
{
    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        logger.LogDebug($"CurseForge 解析 {entry.ProjectId}（{entry.FileName}）");
        var url = $"{configuration.Mirrors.CurseForgeApiMirror.TrimEnd('/')}/v1/mods/{Uri.EscapeDataString(entry.ProjectId)}/files"
            + $"?gameVersion={Uri.EscapeDataString(minecraftVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent.Value);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        McimCachePolicy.EnsureFresh(response, configuration.Update);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var files = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        if (files.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning($"CurseForge 返回非预期结构: {entry.Name}");
            throw new InvalidOperationException($"Unexpected CurseForge response for {entry.Name}.");
        }

        var compatibleFiles = files.EnumerateArray()
            .Where(file => IsCompatible(file, minecraftVersion, loader))
            .OrderByDescending(GetFileDate)
            .ToArray();
        if (compatibleFiles.Length == 0)
        {
            logger.LogWarning($"CurseForge 未找到 MC {minecraftVersion} 的 NeoForge 文件: {entry.Name}（{entry.ProjectId}）");
            throw new InvalidOperationException($"No CurseForge NeoForge {minecraftVersion} files for {entry.Name} ({entry.ProjectId}).");
        }

        JsonElement? selected = compatibleFiles.FirstOrDefault(file => MatchesVersion(entry, file));
        if (selected is null || selected.Value.ValueKind == JsonValueKind.Undefined)
        {
            selected = compatibleFiles[0];
        }

        if (selected is null || selected.Value.ValueKind == JsonValueKind.Undefined)
        {
            logger.LogWarning($"无法定位 CurseForge 文件: {entry.Name}（{entry.ProjectId}）");
            throw new InvalidOperationException($"Unable to resolve CurseForge file for {entry.Name} ({entry.ProjectId}).");
        }

        var selectedFile = selected.Value;
        var fileName = TryGetString(selectedFile, "fileName") ?? TryGetString(selectedFile, "displayName") ?? entry.FileName;
        var urlValue = TryGetString(selectedFile, "downloadUrl") ?? TryGetString(selectedFile, "downloadURL");
        if (string.IsNullOrWhiteSpace(urlValue))
        {
            var fileId = selectedFile.TryGetProperty("id", out var idElement) ? idElement.GetInt64() : 0;
            urlValue = $"{configuration.Mirrors.CurseForgeApiMirror.TrimEnd('/')}/v1/mods/{entry.ProjectId}/files/{fileId}/download";
        }

        var sha1 = "";
        if (selectedFile.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
        {
            foreach (var hash in hashes.EnumerateArray())
            {
                var algo = hash.TryGetProperty("algo", out var algoElement) ? algoElement.GetInt32() : -1;
                if (algo == 1)
                {
                    sha1 = TryGetString(hash, "value") ?? "";
                    break;
                }
            }
        }

        var size = selectedFile.TryGetProperty("fileLength", out var fileLength) && fileLength.TryGetInt64(out var parsedSize)
            ? parsedSize
            : entry.FileSize;
        return new ResolvedModFile(entry, fileName, size, sha1, mirrorRouter.RewriteCurseForgeFileToMirror(new Uri(urlValue)));
    }

    private static bool IsCompatible(JsonElement file, string minecraftVersion, string loader)
    {
        if (!file.TryGetProperty("gameVersions", out var gameVersions) || gameVersions.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        return gameVersions.EnumerateArray().Any(version => version.GetString()?.Equals(minecraftVersion, StringComparison.OrdinalIgnoreCase) == true)
            && gameVersions.EnumerateArray().Any(version => IsLoaderVersion(version.GetString(), loader));
    }

    private static bool IsLoaderVersion(string? value, string loader)
    {
        return value?.Equals(loader, StringComparison.OrdinalIgnoreCase) == true
            || (loader.Equals("neoforge", StringComparison.OrdinalIgnoreCase)
                && value?.Equals("NeoForge", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesVersion(RemoteModEntry entry, JsonElement file)
    {
        if (string.IsNullOrWhiteSpace(entry.VersionLabel))
        {
            var fileName = TryGetString(file, "fileName") ?? "";
            return string.IsNullOrWhiteSpace(entry.FileName)
                || fileName.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase)
                || StripChinesePrefix(fileName).Equals(StripChinesePrefix(entry.FileName), StringComparison.OrdinalIgnoreCase);
        }

        var label = entry.VersionLabel;
        return TryGetString(file, "displayName")?.Equals(label, StringComparison.OrdinalIgnoreCase) == true
            || TryGetString(file, "fileName")?.Equals(label, StringComparison.OrdinalIgnoreCase) == true
            || TryGetString(file, "displayName")?.Contains(label, StringComparison.OrdinalIgnoreCase) == true
            || TryGetString(file, "fileName")?.Contains(label, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static DateTimeOffset GetFileDate(JsonElement file)
    {
        return TryGetString(file, "fileDate") is { } value && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string StripChinesePrefix(string value)
    {
        var index = value.LastIndexOf(']');
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..].Trim() : value.Trim();
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
