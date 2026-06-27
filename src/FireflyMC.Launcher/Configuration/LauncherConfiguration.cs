using System.Text.Json;
using System.Text.Json.Serialization;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Configuration;

public sealed class LauncherConfiguration
{
    public MicrosoftAuthOptions MicrosoftAuth { get; init; } = new();
    public CurseForgeOptions CurseForge { get; init; } = new();
    public SelfUpdateOptions SelfUpdate { get; init; } = new();
    public GameOptions Game { get; init; } = new();
    public MirrorOptions Mirrors { get; init; } = new();
    public UpdateOptions Update { get; init; } = new();
    public FireflyApiOptions FireflyApi { get; init; } = new();

    public static LauncherConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LauncherConfiguration();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, JsonContext.Default.LauncherConfiguration)
            ?? new LauncherConfiguration();
    }
}

public sealed class MicrosoftAuthOptions
{
    public string ClientId { get; init; } = "";
    public string Tenant { get; init; } = "consumers";
    public IReadOnlyList<string> Scopes { get; init; } = ["XboxLive.signin", "offline_access"];
}

public sealed class CurseForgeOptions
{
    public string ApiKey { get; init; } = "";
    public string UserAgent { get; init; } = "FireflyMC-Launcher";
}

public sealed class SelfUpdateOptions
{
    public string ReleasesApi { get; init; } = "https://api.github.com/repos/Sakura520222/FireflyMC-Launcher/releases";
    public string Channel { get; init; } = "stable";
    public string PublicKey { get; init; } = "";
}

public sealed class GameOptions
{
    public string MinecraftVersion { get; init; } = "1.21.1";
    public string NeoForgeVersion { get; init; } = "21.1.219";
    public GameServerSpec Server { get; init; } = new("gm.rainplay.cn", 32772);
}

public sealed class MirrorOptions
{
    public string MinecraftPrimary { get; init; } = "https://piston-meta.mojang.com";
    public string MinecraftFallback { get; init; } = "https://bmclapi2.bangbang93.com";
    public string NeoForgePrimary { get; init; } = "https://maven.neoforged.net";
    public string NeoForgeFallback { get; init; } = "https://bmclapi2.bangbang93.com/maven";
    public string AdoptiumPrimary { get; init; } = "https://api.adoptium.net";
    public string AdoptiumFallback { get; init; } = "https://mirrors.tuna.tsinghua.edu.cn/Adoptium";
}

public sealed class UpdateOptions
{
    public int MaxRetries { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int PerFileTimeoutSeconds { get; init; } = 60;
    public int ResolveFailureThresholdPercent { get; init; } = 10;
}

public sealed class FireflyApiOptions
{
    public string Version { get; init; } = "https://mc.firefly520.top/api/version";
    public string PackMods { get; init; } = "https://mc.firefly520.top/api/pack/mods";
}

[JsonSerializable(typeof(LauncherConfiguration))]
[JsonSerializable(typeof(JavaRuntimeSpec))]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(IReadOnlyList<FireflyMC.Launcher.Models.Accounts.AccountProfile>))]
[JsonSerializable(typeof(FireflyMC.Launcher.Models.Accounts.MicrosoftCredential))]
[JsonSerializable(typeof(FireflyMC.Launcher.Models.Installed.InstalledManifest))]
[JsonSerializable(typeof(FireflyMC.Launcher.Models.TransactionState))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class JsonContext : JsonSerializerContext;
