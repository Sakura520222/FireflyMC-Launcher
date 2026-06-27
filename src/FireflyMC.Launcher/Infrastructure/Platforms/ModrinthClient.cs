using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class ModrinthClient(
    HttpClient httpClient,
    LauncherConfiguration configuration,
    MirrorRouter mirrorRouter,
    LauncherUserAgent userAgent) : IModPlatformClient
{
    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var source in new[]
        {
            new ModrinthSource(configuration.Mirrors.ModrinthApiPrimary, UseMirrorFiles: false, IsMcim: false),
            new ModrinthSource(configuration.Mirrors.ModrinthApiMirror, UseMirrorFiles: true, IsMcim: true)
        })
        {
            try
            {
                var resolved = await TryResolveAsync(source, entry, minecraftVersion, loader, cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(ex);
            }
        }

        throw new InvalidOperationException($"Unable to resolve Modrinth file for {entry.Name} ({entry.ProjectId}).", errors.FirstOrDefault());
    }

    private async Task<ResolvedModFile?> TryResolveAsync(
        ModrinthSource source,
        RemoteModEntry entry,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken)
    {
        var url = BuildVersionsUri(source.ApiBase, entry.ProjectId, minecraftVersion, loader);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent.Value);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (source.IsMcim)
        {
            McimCachePolicy.EnsureFresh(response, configuration.Update);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var version in document.RootElement.EnumerateArray())
        {
            if (!MatchesVersion(entry, version))
            {
                continue;
            }

            var file = PickPrimaryFile(entry, version);
            if (file is not null)
            {
                return RewriteDownloadUri(file with { Entry = entry }, source);
            }
        }

        foreach (var version in document.RootElement.EnumerateArray())
        {
            var file = PickPrimaryFile(entry, version);
            if (file is not null)
            {
                return RewriteDownloadUri(file with { Entry = entry }, source);
            }
        }

        return null;
    }

    private ResolvedModFile RewriteDownloadUri(ResolvedModFile resolved, ModrinthSource source)
    {
        return source.UseMirrorFiles
            ? resolved with { DownloadUri = mirrorRouter.RewriteModrinthFileToMirror(resolved.DownloadUri) }
            : resolved;
    }

    private static Uri BuildVersionsUri(string apiBase, string projectId, string minecraftVersion, string loader)
    {
        var root = apiBase.TrimEnd('/');
        var loaderQuery = Uri.EscapeDataString($"[\"{loader}\"]");
        var gameVersionQuery = Uri.EscapeDataString($"[\"{minecraftVersion}\"]");
        return new Uri($"{root}/v2/project/{Uri.EscapeDataString(projectId)}/version?loaders={loaderQuery}&game_versions={gameVersionQuery}");
    }

    private static bool MatchesVersion(RemoteModEntry entry, JsonElement version)
    {
        if (string.IsNullOrWhiteSpace(entry.VersionLabel))
        {
            return true;
        }

        var label = entry.VersionLabel;
        return TryGetString(version, "version_number")?.Equals(label, StringComparison.OrdinalIgnoreCase) == true
            || TryGetString(version, "name")?.Contains(label, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static ResolvedModFile? PickPrimaryFile(RemoteModEntry entry, JsonElement version)
    {
        if (!version.TryGetProperty("files", out var files))
        {
            return null;
        }

        JsonElement? selected = null;
        foreach (var file in files.EnumerateArray())
        {
            var filename = TryGetString(file, "filename") ?? "";
            if (string.Equals(filename, entry.FileName, StringComparison.OrdinalIgnoreCase))
            {
                selected = file;
                break;
            }

            if (selected is null && file.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            {
                selected = file;
            }
        }

        selected ??= files.EnumerateArray().FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        var selectedFile = selected.Value;
        var fileName = TryGetString(selectedFile, "filename") ?? entry.FileName;
        var url = TryGetString(selectedFile, "url") ?? throw new InvalidOperationException($"Modrinth file has no URL: {entry.Name}");
        var sha1 = selectedFile.TryGetProperty("hashes", out var hashes)
            ? TryGetString(hashes, "sha1") ?? ""
            : "";
        var size = selectedFile.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
            ? parsedSize
            : entry.FileSize;

        return new ResolvedModFile(entry, fileName, size, sha1, new Uri(url));
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed record ModrinthSource(string ApiBase, bool UseMirrorFiles, bool IsMcim);
}
