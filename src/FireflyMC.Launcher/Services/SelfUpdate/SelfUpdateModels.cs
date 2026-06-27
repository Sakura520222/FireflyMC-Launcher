namespace FireflyMC.Launcher.Services.SelfUpdate;

public sealed record LauncherUpdateInfo(
    Version Version,
    string Tag,
    Uri PackageUri,
    Uri SignatureUri,
    string ReleaseNotes,
    bool IsPrerelease);
