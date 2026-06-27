using System.Reflection;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Minecraft;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Infrastructure.Minecraft;

public sealed class AdoptiumJavaRuntimeInstallerTests
{
    [Fact]
    public async Task ReplaceJavaRuntimeAsync_WhenDestinationIsTemporarilyLocked_RetriesAndSucceeds()
    {
        var root = CreateTempRoot();
        var paths = new LauncherPaths(root);
        var source = Path.Combine(root, "runtime", "java-new");
        var destination = paths.JavaRuntimeDirectory;
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "java.exe"), "new");
        var lockedPath = Path.Combine(destination, "locked.dll");
        await File.WriteAllTextAsync(lockedPath, "old");

        await using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releaseLock = Task.Run(async () =>
        {
            await Task.Delay(200);
            await locked.DisposeAsync();
        });
        var installer = new AdoptiumJavaRuntimeInstaller(paths, new NoopDownloader(), new NullDiagnosticLogger());
        var method = typeof(AdoptiumJavaRuntimeInstaller).GetMethod("ReplaceJavaRuntimeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(installer, [source, destination, CancellationToken.None])!;
        await task;
        await releaseLock;

        File.Exists(Path.Combine(destination, "java.exe")).Should().BeTrue();
        Directory.Exists(source).Should().BeFalse();
    }

    [Fact]
    public async Task InstallAsync_WhenJavaAlreadyInstalled_RemovesStaleJavaTempDirectories()
    {
        var root = CreateTempRoot();
        var paths = new LauncherPaths(root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.JavaRuntimeDirectory)!);
        Directory.CreateDirectory(Path.Combine(paths.JavaRuntimeDirectory, "bin"));
        await File.WriteAllTextAsync(paths.JavaExecutable(), "installed");
        var stale = Path.Combine(paths.RuntimeDirectory, $"java-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "leftover.txt"), "stale");

        var installer = new AdoptiumJavaRuntimeInstaller(paths, new NoopDownloader(), new NullDiagnosticLogger());

        await installer.InstallAsync(new JavaRuntimeSpec("eclipse", 21, "21.0.8+9", "jre", "https://example.test/java.zip"), null, CancellationToken.None);

        Directory.Exists(stale).Should().BeFalse();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adoptium-installer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class NoopDownloader : IDownloader
    {
        public Task DownloadAsync(Uri uri, string destinationPath, bool resume, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}

internal static class LauncherPathsTestExtensions
{
    public static string JavaExecutable(this LauncherPaths paths)
    {
        return Path.Combine(paths.JavaRuntimeDirectory, "bin", "java.exe");
    }
}
