using System.Text.Json;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
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
    IGameLogService logService,
    IDiagnosticLogger logger) : ILaunchService
{
    public async Task<LaunchProfile> BuildLaunchProfileAsync(AccountProfile account, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var session = await accountService.GetOrRefreshSessionAsync(account, cancellationToken);
        if (account.Type == AccountType.Microsoft && session?.MinecraftAccessToken is null)
        {
            logger.LogError($"Microsoft 账号 {account.Id} 会话不可用");
            throw new InvalidOperationException("Microsoft 账号会话不可用，请重新登录。");
        }

        var versionJson = FindVersionJson();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(versionJson, cancellationToken));
        var root = document.RootElement;
        using var inheritedDocument = await LoadInheritedVersionDocumentAsync(root, cancellationToken);
        var inheritedRoot = inheritedDocument?.RootElement;
        var versionId = TryGetString(root, "id") ?? TryGetString(inheritedRoot, "id") ?? configuration.Game.MinecraftVersion;
        var mainClass = TryGetString(root, "mainClass") ?? TryGetString(inheritedRoot, "mainClass") ?? "net.minecraft.client.main.Main";
        var assetIndex = TryGetAssetIndex(root) ?? TryGetAssetIndex(inheritedRoot) ?? configuration.Game.MinecraftVersion;
        var classpath = BuildClasspath((GetInheritedVersionJson(root), inheritedRoot), (versionJson, root));
        var java = string.IsNullOrWhiteSpace(settings.JavaPathOverride)
            ? javaRuntimeInstaller.JavaExecutable
            : settings.JavaPathOverride!;
        if (!File.Exists(java))
        {
            logger.LogError($"Java 可执行文件不存在: {java}");
            throw new FileNotFoundException("Java executable not found.", java);
        }

        var token = account.Type == AccountType.Offline ? "0" : session!.MinecraftAccessToken!;
        var variables = CreateArgumentVariables(account, versionId, assetIndex, token, classpath);
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

        if (inheritedRoot is not null)
        {
            AddVersionArguments(root, "jvm", jvmArgs, variables);
        }

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
        if (inheritedRoot is not null)
        {
            AddVersionArguments(root, "game", gameArgs, variables);
        }

        if (settings.AutoJoinServer)
        {
            gameArgs.Add("--server");
            gameArgs.Add(configuration.Game.Server.Host);
            gameArgs.Add("--port");
            gameArgs.Add(configuration.Game.Server.Port.ToString());
        }

        logger.LogInformation($"已为账号 {account.Id} 构建启动配置（内存 {settings.MinMemoryMb}-{settings.MaxMemoryMb}MB，自动连服 {settings.AutoJoinServer}）");
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
        logger.LogInformation($"启动 Minecraft，账号 {account.Id}");
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

        logger.LogError($"未找到 version.json（MC {configuration.Game.MinecraftVersion} / NeoForge {configuration.Game.NeoForgeVersion}）");
        throw new FileNotFoundException("未找到 Minecraft version.json，请先安装游戏。", vanilla);
    }

    private async Task<JsonDocument?> LoadInheritedVersionDocumentAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var inheritedVersionJson = GetInheritedVersionJson(root);
        if (inheritedVersionJson is null)
        {
            return null;
        }

        return JsonDocument.Parse(await File.ReadAllTextAsync(inheritedVersionJson, cancellationToken));
    }

    private string? GetInheritedVersionJson(JsonElement root)
    {
        var inheritedVersion = TryGetString(root, "inheritsFrom");
        if (string.IsNullOrWhiteSpace(inheritedVersion))
        {
            return null;
        }

        var inheritedVersionJson = Path.Combine(paths.VersionsDirectory, inheritedVersion, $"{inheritedVersion}.json");
        if (File.Exists(inheritedVersionJson))
        {
            return inheritedVersionJson;
        }

        logger.LogError($"未找到继承的 version.json: {inheritedVersion}");
        throw new FileNotFoundException("未找到继承的 Minecraft version.json，请重新安装游戏。", inheritedVersionJson);
    }

    private IReadOnlyList<string> BuildClasspath(params (string? VersionJson, JsonElement? Root)[] versions)
    {
        var classpath = new List<string>();
        foreach (var (_, root) in versions)
        {
            if (root is not { } versionRoot || !versionRoot.TryGetProperty("libraries", out var libraries))
            {
                continue;
            }

            foreach (var library in libraries.EnumerateArray())
            {
                if (library.TryGetProperty("downloads", out var downloads)
                    && downloads.TryGetProperty("artifact", out var artifact)
                    && TryGetString(artifact, "path") is { } relative)
                {
                    var path = Path.Combine(paths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(path) && !classpath.Contains(path))
                    {
                        classpath.Add(path);
                    }
                }
            }
        }

        foreach (var (versionJson, _) in versions)
        {
            if (versionJson is null)
            {
                continue;
            }

            var jar = Path.ChangeExtension(versionJson, ".jar");
            if (File.Exists(jar) && !classpath.Contains(jar))
            {
                classpath.Add(jar);
            }
        }

        return classpath;
    }

    private Dictionary<string, string> CreateArgumentVariables(
        AccountProfile account,
        string versionId,
        string assetIndex,
        string token,
        IReadOnlyList<string> classpath)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = account.Username,
            ["version_name"] = versionId,
            ["game_directory"] = paths.MinecraftDirectory,
            ["assets_root"] = paths.AssetsDirectory,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = account.Uuid,
            ["auth_access_token"] = token,
            ["user_type"] = account.Type == AccountType.Microsoft ? "msa" : "legacy",
            ["version_type"] = "FireflyMC",
            ["natives_directory"] = Path.Combine(paths.VersionsDirectory, "natives"),
            ["launcher_name"] = "FireflyMC-Launcher",
            ["launcher_version"] = "1.0.0",
            ["library_directory"] = paths.LibrariesDirectory,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["classpath"] = string.Join(Path.PathSeparator, classpath)
        };
    }

    private static void AddVersionArguments(
        JsonElement root,
        string section,
        List<string> arguments,
        IReadOnlyDictionary<string, string> variables)
    {
        if (!root.TryGetProperty("arguments", out var versionArguments)
            || !versionArguments.TryGetProperty(section, out var sectionArguments)
            || sectionArguments.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var argument in sectionArguments.EnumerateArray())
        {
            AddArgumentValue(argument, arguments, variables);
        }
    }

    private static void AddArgumentValue(
        JsonElement argument,
        List<string> arguments,
        IReadOnlyDictionary<string, string> variables)
    {
        if (argument.ValueKind == JsonValueKind.String)
        {
            arguments.Add(ApplyVariables(argument.GetString()!, variables));
            return;
        }

        if (argument.ValueKind != JsonValueKind.Object
            || !argument.TryGetProperty("value", out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            arguments.Add(ApplyVariables(value.GetString()!, variables));
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                arguments.Add(ApplyVariables(item.GetString()!, variables));
            }
        }
    }

    private static string ApplyVariables(string argument, IReadOnlyDictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            argument = argument.Replace($"${{{key}}}", value, StringComparison.Ordinal);
        }

        return argument;
    }

    private static string? TryGetAssetIndex(JsonElement? root)
    {
        if (root is not { } versionRoot)
        {
            return null;
        }

        return versionRoot.TryGetProperty("assetIndex", out var assetIndex)
            ? TryGetString(assetIndex, "id")
            : TryGetString(versionRoot, "assets");
    }

    private static IEnumerable<string> SplitArguments(string arguments)
    {
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? TryGetString(JsonElement? element, string property)
    {
        return element is { ValueKind: JsonValueKind.Object }
            && element.Value.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
