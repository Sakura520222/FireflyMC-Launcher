using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Minecraft;
using FireflyMC.Launcher.Infrastructure.Process;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Services.Accounts;
using FireflyMC.Launcher.Services.Logging;

namespace FireflyMC.Launcher.Services.Launch;

public sealed class LaunchService(
    LauncherConfiguration configuration,
    ILauncherPaths paths,
    ISettingsStore settingsStore,
    IAccountService accountService,
    AdoptiumJavaRuntimeInstaller javaRuntimeInstaller,
    GameProcess gameProcess,
    IGameLogService logService) : ILaunchService
{
    public async Task<LaunchProfile> BuildLaunchProfileAsync(AccountProfile account, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var session = await accountService.GetOrRefreshSessionAsync(account, cancellationToken);
        if (account.Type == AccountType.Microsoft && session?.MinecraftAccessToken is null)
        {
            throw new InvalidOperationException("Microsoft 账号会话不可用，请重新登录。");
        }

        var versionJson = FindVersionJson();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(versionJson, cancellationToken));
        var root = document.RootElement;
        var versionId = TryGetString(root, "id") ?? configuration.Game.MinecraftVersion;
        var mainClass = TryGetString(root, "mainClass") ?? "net.minecraft.client.main.Main";
        var assetIndex = TryGetString(root.GetProperty("assetIndex"), "id") ?? configuration.Game.MinecraftVersion;
        var classpath = BuildClasspath(versionJson, root);
        var java = string.IsNullOrWhiteSpace(settings.JavaPathOverride)
            ? javaRuntimeInstaller.JavaExecutable
            : settings.JavaPathOverride!;
        if (!File.Exists(java))
        {
            throw new FileNotFoundException("Java executable not found.", java);
        }

        var jvmArgs = new List<string>
        {
            $"-Xms{settings.MinMemoryMb}m",
            $"-Xmx{settings.MaxMemoryMb}m",
            "-Djava.library.path=" + Path.Combine(paths.VersionsDirectory, "natives"),
            "-Dminecraft.launcher.brand=FireflyMC-Launcher",
            "-Dminecraft.launcher.version=1.0.0"
        };
        if (!string.IsNullOrWhiteSpace(settings.AdditionalJvmArgs))
        {
            jvmArgs.AddRange(SplitArguments(settings.AdditionalJvmArgs));
        }

        var token = account.Type == AccountType.Offline ? "0" : session!.MinecraftAccessToken!;
        var gameArgs = new List<string>
        {
            "--username", account.Username,
            "--version", versionId,
            "--gameDir", paths.MinecraftDirectory,
            "--assetsDir", paths.AssetsDirectory,
            "--assetIndex", assetIndex,
            "--uuid", account.Uuid,
            "--accessToken", token,
            "--userType", account.Type == AccountType.Microsoft ? "msa" : "legacy",
            "--versionType", "FireflyMC"
        };
        if (settings.AutoJoinServer)
        {
            gameArgs.Add("--server");
            gameArgs.Add(configuration.Game.Server.Host);
            gameArgs.Add("--port");
            gameArgs.Add(configuration.Game.Server.Port.ToString());
        }

        return new LaunchProfile(
            java,
            jvmArgs,
            gameArgs,
            mainClass,
            paths.MinecraftDirectory,
            classpath,
            Path.Combine(paths.VersionsDirectory, "natives"),
            null);
    }

    public async Task LaunchAsync(AccountProfile account, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var profile = await BuildLaunchProfileAsync(account, cancellationToken);
        logService.Append("正在启动 Minecraft...");
        gameProcess.Start(profile, redactIpAddresses: true);
        if (settings.MinimizeOnGameLaunch)
        {
            System.Windows.Application.Current?.MainWindow?.Dispatcher.Invoke(() => System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized);
        }
    }

    private string FindVersionJson()
    {
        var neoForge = Directory.Exists(paths.VersionsDirectory)
            ? Directory.EnumerateFiles(paths.VersionsDirectory, "*.json", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileNameWithoutExtension(path).Contains(configuration.Game.NeoForgeVersion, StringComparison.OrdinalIgnoreCase))
            : null;
        if (neoForge is not null)
        {
            return neoForge;
        }

        var vanilla = Path.Combine(paths.VersionsDirectory, configuration.Game.MinecraftVersion, $"{configuration.Game.MinecraftVersion}.json");
        if (File.Exists(vanilla))
        {
            return vanilla;
        }

        throw new FileNotFoundException("未找到 Minecraft version.json，请先安装游戏。", vanilla);
    }

    private IReadOnlyList<string> BuildClasspath(string versionJson, JsonElement root)
    {
        var classpath = new List<string>();
        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateArray())
            {
                if (library.TryGetProperty("downloads", out var downloads)
                    && downloads.TryGetProperty("artifact", out var artifact)
                    && TryGetString(artifact, "path") is { } relative)
                {
                    var path = Path.Combine(paths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(path))
                    {
                        classpath.Add(path);
                    }
                }
            }
        }

        var jar = Path.ChangeExtension(versionJson, ".jar");
        if (File.Exists(jar))
        {
            classpath.Add(jar);
        }

        return classpath;
    }

    private static IEnumerable<string> SplitArguments(string arguments)
    {
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? TryGetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
