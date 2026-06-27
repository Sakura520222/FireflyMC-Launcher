using System.Collections.ObjectModel;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Minecraft;
using FireflyMC.Launcher.Infrastructure.Process;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Models.Remote;
using FireflyMC.Launcher.Services.Accounts;
using FireflyMC.Launcher.Services.Accounts.Microsoft;
using FireflyMC.Launcher.Services.Launch;
using FireflyMC.Launcher.Services.Logging;

namespace FireflyMC.Launcher.Tests;

public sealed class LaunchServiceTests
{
    [Fact]
    public async Task BuildLaunchProfileAsync_WhenNeoForgeVersionInheritsVanilla_CombinesChildAndParentMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"firefly-launch-{Guid.NewGuid():N}");
        try
        {
            var paths = new LauncherPaths(root);
            paths.EnsureCreated();
            var java = Path.Combine(paths.JavaRuntimeDirectory, "bin", "java.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(java)!);
            await File.WriteAllTextAsync(java, "", CancellationToken.None);

            var parentLibrary = CreateLibrary(paths, "com/mojang/logging/1.2.7/logging-1.2.7.jar");
            var childLibrary = CreateLibrary(paths, "cpw/mods/bootstraplauncher/2.0.2/bootstraplauncher-2.0.2.jar");
            var vanillaJar = Path.Combine(paths.VersionsDirectory, "1.21.1", "1.21.1.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(vanillaJar)!);
            await File.WriteAllTextAsync(vanillaJar, "jar", CancellationToken.None);

            await WriteVersionJsonAsync(
                Path.Combine(paths.VersionsDirectory, "1.21.1", "1.21.1.json"),
                """
                {
                  "id": "1.21.1",
                  "assetIndex": { "id": "17" },
                  "mainClass": "net.minecraft.client.main.Main",
                  "libraries": [
                    { "downloads": { "artifact": { "path": "com/mojang/logging/1.2.7/logging-1.2.7.jar" } } }
                  ]
                }
                """);
            await WriteVersionJsonAsync(
                Path.Combine(paths.VersionsDirectory, "neoforge-21.1.219", "neoforge-21.1.219.json"),
                """
                {
                  "id": "neoforge-21.1.219",
                  "inheritsFrom": "1.21.1",
                  "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
                  "arguments": {
                    "game": ["--launchTarget", "forgeclient"],
                    "jvm": ["-DlibraryDirectory=${library_directory}"]
                  },
                  "libraries": [
                    { "downloads": { "artifact": { "path": "cpw/mods/bootstraplauncher/2.0.2/bootstraplauncher-2.0.2.jar" } } }
                  ]
                }
                """);

            var service = CreateService(paths, java);
            var account = new AccountProfile("offline:test", AccountType.Offline, "Firefly", "00000000000000000000000000000000", DateTimeOffset.UtcNow);

            var profile = await service.BuildLaunchProfileAsync(account, CancellationToken.None);

            Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", profile.MainClass);
            Assert.Equal("17", ValueAfter(profile.GameArguments, "--assetIndex"));
            Assert.Equal("forgeclient", ValueAfter(profile.GameArguments, "--launchTarget"));
            Assert.Contains(parentLibrary, profile.ClasspathEntries);
            Assert.Contains(childLibrary, profile.ClasspathEntries);
            Assert.Contains(vanillaJar, profile.ClasspathEntries);
            Assert.Contains($"-DlibraryDirectory={paths.LibrariesDirectory}", profile.JvmArguments);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LaunchService CreateService(LauncherPaths paths, string java)
    {
        var configuration = new LauncherConfiguration
        {
            Game = new GameOptions
            {
                MinecraftVersion = "1.21.1",
                NeoForgeVersion = "21.1.219",
                Server = new GameServerSpec("localhost", 25565)
            }
        };
        var logger = new NullDiagnosticLogger();
        var logService = new TestGameLogService();
        return new LaunchService(
            configuration,
            paths,
            new TestSettingsStore(new LauncherSettings { AutoJoinServer = false, JavaPathOverride = java }),
            new TestAccountService(),
            new AdoptiumJavaRuntimeInstaller(paths, new TestDownloader(), logger),
            new GameProcess(logService, logger),
            logService,
            logger);
    }

    private static async Task WriteVersionJsonAsync(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, CancellationToken.None);
    }

    private static string CreateLibrary(LauncherPaths paths, string relativePath)
    {
        var path = Path.Combine(paths.LibrariesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "jar");
        return path;
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, string key)
    {
        var index = arguments.ToList().IndexOf(key);
        Assert.True(index >= 0 && index + 1 < arguments.Count, $"未找到参数 {key} 的值。");
        return arguments[index + 1];
    }

    private sealed class TestSettingsStore(LauncherSettings settings) : ISettingsStore
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestAccountService : IAccountService
    {
        public Task<IReadOnlyList<AccountProfile>> GetAccountsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IDeviceCodeLoginSession> StartMicrosoftLoginAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountProfile> CompleteMicrosoftLoginAsync(IDeviceCodeLoginSession session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountProfile> AddOfflineAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task LogoutAsync(string accountId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountSession?> GetOrRefreshSessionAsync(AccountProfile profile, CancellationToken cancellationToken)
        {
            return Task.FromResult<AccountSession?>(new AccountSession(profile.Id, null, null, null, null));
        }
    }

    private sealed class TestDownloader : IDownloader
    {
        public Task DownloadAsync(
            Uri uri,
            string destinationPath,
            bool resume,
            IProgress<StageProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestGameLogService : IGameLogService
    {
        public ObservableCollection<string> Lines { get; } = [];

        public void Append(string line)
        {
            Lines.Add(line);
        }

        public void Clear()
        {
            Lines.Clear();
        }
    }
}
