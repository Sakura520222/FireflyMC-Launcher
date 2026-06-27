using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Models.Installed;

public sealed record InstalledManifest(
    int SchemaVersion,
    string LastInstalledPackVersion,
    string RemoteManifestSha256,
    DateTimeOffset InstalledAt,
    string MinecraftVersion,
    string NeoForgeVersion,
    string JavaRuntimeVersion,
    IReadOnlyList<InstalledFile> ManagedFiles,
    FireflyModEntry FireflyMod);

public sealed record InstalledFile(string RelativePath, long Size, string Sha1);
