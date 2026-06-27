namespace FireflyMC.Launcher.Models.Remote;

public enum ModPlatform
{
    Modrinth,
    CurseForge
}

public sealed record RemoteManifest(
    int SchemaVersion,
    string PackVersion,
    string ManifestId,
    string ManifestSha256,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RemoteModEntry> Mods,
    FireflyModEntry FireflyMod,
    JavaRuntimeSpec Java,
    GameServerSpec Server,
    bool ForceUpdate);

public sealed record RemoteModEntry(
    string Name,
    string FileName,
    long FileSize,
    ModPlatform Platform,
    string ProjectId,
    string? VersionLabel,
    bool Required = true);

public sealed record FireflyModEntry(string Version, string DownloadUrl, string? Sha256);

public sealed record JavaRuntimeSpec(
    string Vendor,
    int Major,
    string RuntimeVersion,
    string ImageType,
    string Sha256,
    string Url);

public sealed record GameServerSpec(string Host, int Port);
