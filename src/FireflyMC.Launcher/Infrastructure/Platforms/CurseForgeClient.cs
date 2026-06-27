using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Infrastructure.Platforms;

public sealed class CurseForgeClient(HttpClient httpClient, CurseForgeOptions options) : IModPlatformClient
{
    public async Task<ResolvedModFile> ResolveAsync(RemoteModEntry entry, string minecraftVersion, string loader, CancellationToken cancellationToken)
    {
        var url = $"https://bmclapi2.bangbang93.com/curseforge/v1/mods/{Uri.EscapeDataString(entry.ProjectId)}/files"
            + $"?gameVersion={Uri.EscapeDataString(minecraftVersion)}&modLoaderType=6";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var files = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        if (files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Unexpected CurseForge response for {entry.Name}.");
        }

        JsonElement? selected = null;
        foreach (var file in files.EnumerateArray())
        {
            if (!MatchesVersion(entry, file))
            {
                continue;
            }

            selected = file;
            break;
        }

        selected ??= files.EnumerateArray().FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException($"Unable to resolve CurseForge file for {entry.Name} ({entry.ProjectId}).");
        }

        var selectedFile = selected.Value;
        var fileName = TryGetString(selectedFile, "fileName") ?? TryGetString(selectedFile, "displayName") ?? entry.FileName;
        var urlValue = TryGetString(selectedFile, "downloadUrl") ?? TryGetString(selectedFile, "downloadURL");
        if (string.IsNullOrWhiteSpace(urlValue))
        {
            var fileId = selectedFile.TryGetProperty("id", out var idElement) ? idElement.GetInt64() : 0;
            urlValue = $"https://bmclapi2.bangbang93.com/curseforge/v1/mods/{entry.ProjectId}/files/{fileId}/download";
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
        return new ResolvedModFile(entry, fileName, size, sha1, new Uri(urlValue));
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
        return TryGetString(file, "displayName")?.Contains(label, StringComparison.OrdinalIgnoreCase) == true
            || TryGetString(file, "fileName")?.Contains(label, StringComparison.OrdinalIgnoreCase) == true;
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
