using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Services.SelfUpdate;

public sealed class SelfUpdateService(
    HttpClient httpClient,
    LauncherConfiguration configuration,
    ILauncherPaths paths,
    IDownloader downloader) : ISelfUpdateService
{
    public async Task<LauncherUpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, configuration.SelfUpdate.ReleasesApi);
        request.Headers.UserAgent.ParseAdd("FireflyMC-Launcher");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            var prerelease = release.TryGetProperty("prerelease", out var prereleaseElement) && prereleaseElement.GetBoolean();
            if (configuration.SelfUpdate.Channel.Equals("stable", StringComparison.OrdinalIgnoreCase) && prerelease)
            {
                continue;
            }

            var tag = TryGetString(release, "tag_name") ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= current)
            {
                continue;
            }

            if (!release.TryGetProperty("assets", out var assets))
            {
                continue;
            }

            Uri? package = null;
            Uri? signature = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = TryGetString(asset, "name") ?? "";
                var url = TryGetString(asset, "browser_download_url");
                if (url is null)
                {
                    continue;
                }

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    package = new Uri(url);
                }
                else if (name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                {
                    signature = new Uri(url);
                }
            }

            if (package is not null && signature is not null)
            {
                return new LauncherUpdateInfo(version, tag, package, signature, TryGetString(release, "body") ?? "", prerelease);
            }
        }

        return null;
    }

    public async Task StartUpdateAsync(LauncherUpdateInfo updateInfo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.SelfUpdate.PublicKey))
        {
            throw new InvalidOperationException("SelfUpdate.PublicKey 未配置，拒绝执行自更新。");
        }

        Directory.CreateDirectory(paths.UpdateDirectory);
        var packagePath = Path.Combine(paths.UpdateDirectory, $"launcher-{updateInfo.Tag}.zip");
        var signaturePath = Path.Combine(paths.UpdateDirectory, $"launcher-{updateInfo.Tag}.sig");
        await downloader.DownloadAsync(updateInfo.PackageUri, packagePath, resume: true, new Progress<StageProgress>(), cancellationToken);
        await downloader.DownloadAsync(updateInfo.SignatureUri, signaturePath, resume: true, new Progress<StageProgress>(), cancellationToken);
        var updater = Path.Combine(AppContext.BaseDirectory, "FireflyMC.Updater.exe");
        if (!File.Exists(updater))
        {
            throw new FileNotFoundException("Updater.exe not found.", updater);
        }

        var nonce = Guid.NewGuid().ToString("N");
        var args = new[]
        {
            "--package", packagePath,
            "--signature", signaturePath,
            "--target", paths.RootDirectory,
            "--public-key", configuration.SelfUpdate.PublicKey,
            "--launcher-pid", Environment.ProcessId.ToString(),
            "--nonce", nonce
        };
        Process.Start(new ProcessStartInfo(updater)
        {
            UseShellExecute = false,
            Arguments = string.Join(' ', args.Select(Quote))
        });
        Environment.Exit(0);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
