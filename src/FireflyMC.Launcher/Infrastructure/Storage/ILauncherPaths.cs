namespace FireflyMC.Launcher.Infrastructure.Storage;

public interface ILauncherPaths
{
    string RootDirectory { get; }
    string MinecraftDirectory { get; }
    string RuntimeDirectory { get; }
    string JavaRuntimeDirectory { get; }
    string VersionsDirectory { get; }
    string LibrariesDirectory { get; }
    string AssetsDirectory { get; }
    string ModsDirectory { get; }
    string UpdateDirectory { get; }
    string StagingDirectory { get; }
    string AccountsFile { get; }
    string SettingsFile { get; }
    string InstalledManifestFile { get; }
    string TransactionFile { get; }
    string LogsDirectory { get; }
    string SecretsDirectory { get; }
    string GetAbsoluteGamePath(string relativePath);
    void EnsureCreated();
}
