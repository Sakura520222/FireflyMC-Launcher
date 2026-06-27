using FireflyMC.Launcher.Models.Remote;

namespace FireflyMC.Launcher.Models;

public sealed record ResolvedModFile(
    RemoteModEntry Entry,
    string FileName,
    long Size,
    string Sha1,
    Uri DownloadUri);

public sealed record UpdatePlan(
    string TargetManifestSha256,
    IReadOnlyList<FileToDownload> Downloads,
    IReadOnlyList<FileToDelete> Deletes,
    IReadOnlyList<FileToKeep> Keeps,
    FireflyModAction FireflyModAction,
    bool JavaRuntimeChanged);

public sealed record FileToDownload(string RelativePath, ResolvedModFile Source, bool Required);

public sealed record FileToDelete(string RelativePath);

public sealed record FileToKeep(string RelativePath, string Sha1);

public sealed record FireflyModAction(bool ShouldDownload, Uri? DownloadUri, string? Sha256, string RelativePath);
