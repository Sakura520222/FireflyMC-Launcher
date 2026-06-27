using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Infrastructure.Minecraft;

public sealed class NeoForgeClientInstallerTests
{
    [Fact]
    public async Task InstallAsync_WhenLauncherProfilesMissing_CreatesValidLauncherProfileBeforeRunningInstaller()
    {
        var root = CreateTempRoot();
        var paths = new LauncherPaths(root);
        paths.EnsureCreated();
        var installer = new NeoForgeClientInstaller(
            paths,
            new WritingDownloader(),
            new MavenArtifactResolver(new LauncherConfiguration()),
            new ProcessorRunner(new NullDiagnosticLogger()),
            new NullDiagnosticLogger());
        var profilePath = Path.Combine(paths.MinecraftDirectory, "launcher_profiles.json");

        try
        {
            await installer.InstallAsync("missing-firefly-java.exe", "1.21.1", "21.1.219", useMirror: true, null, CancellationToken.None);
        }
        catch
        {
        }

        File.Exists(profilePath).Should().BeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(profilePath));
        var rootElement = document.RootElement;
        var profile = rootElement.GetProperty("profiles").GetProperty("FireflyMC");
        profile.GetProperty("name").GetString().Should().Be("FireflyMC");
        profile.GetProperty("type").GetString().Should().Be("custom");
        profile.GetProperty("lastVersionId").GetString().Should().Be("1.21.1");
        rootElement.GetProperty("selectedProfile").GetString().Should().Be("FireflyMC");
        rootElement.GetProperty("authenticationDatabase").ValueKind.Should().Be(JsonValueKind.Object);
        rootElement.GetProperty("launcherVersion").GetProperty("name").GetString().Should().Be("FireflyMC Launcher");
        rootElement.GetProperty("settings").GetProperty("enableReleases").GetBoolean().Should().BeTrue();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"neoforge-installer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class WritingDownloader : IDownloader
    {
        public async Task DownloadAsync(Uri uri, string destinationPath, bool resume, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, "installer", cancellationToken);
        }
    }
}
