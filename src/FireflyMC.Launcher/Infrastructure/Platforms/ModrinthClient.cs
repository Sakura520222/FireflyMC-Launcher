using System.Text.Json;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class ModrinthClient(HttpClient httpClient) : IModPlatformClient
{
    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(entry.ProjectId)}/version"
            + $"?loaders=[\"{Uri.EscapeDataString(loader)}\"]&game_versions=[\"{Uri.EscapeDataString(minecraftVersion)}\"]";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("FireflyMC-Launcher");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
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
                return file with { Entry = entry };
            }
        }

        foreach (var version in document.RootElement.EnumerateArray())
        {
            var file = PickPrimaryFile(entry, version);
            if (file is not null)
            {
                return file with { Entry = entry };
            }
        }

        throw new InvalidOperationException($"Unable to resolve Modrinth file for {entry.Name} ({entry.ProjectId}).");
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
}
