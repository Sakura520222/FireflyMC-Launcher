namespace FireflyMC.Launcher.Infrastructure.Storage;

public sealed class LauncherPaths : ILauncherPaths
{
    public LauncherPaths()
        : this(AppContext.BaseDirectory)
    {
    }

    public LauncherPaths(string baseDirectory)
    {
        RootDirectory = Path.GetFullPath(baseDirectory);
        MinecraftDirectory = Path.Combine(RootDirectory, ".minecraft");
        RuntimeDirectory = Path.Combine(RootDirectory, "runtime");
        JavaRuntimeDirectory = Path.Combine(RuntimeDirectory, "java-21");
        VersionsDirectory = Path.Combine(MinecraftDirectory, "versions");
        LibrariesDirectory = Path.Combine(MinecraftDirectory, "libraries");
        AssetsDirectory = Path.Combine(MinecraftDirectory, "assets");
        ModsDirectory = Path.Combine(MinecraftDirectory, "mods");
        UpdateDirectory = Path.Combine(RootDirectory, "update");
        StagingDirectory = Path.Combine(UpdateDirectory, "staging");
        AccountsFile = Path.Combine(RootDirectory, "accounts.json");
        SettingsFile = Path.Combine(RootDirectory, "settings.json");
        InstalledManifestFile = Path.Combine(MinecraftDirectory, "firefly-installed.json");
        TransactionFile = Path.Combine(UpdateDirectory, "transaction.json");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        SecretsDirectory = Path.Combine(RootDirectory, "secrets");
    }

    public string RootDirectory { get; }
    public string MinecraftDirectory { get; }
    public string RuntimeDirectory { get; }
    public string JavaRuntimeDirectory { get; }
    public string VersionsDirectory { get; }
    public string LibrariesDirectory { get; }
    public string AssetsDirectory { get; }
    public string ModsDirectory { get; }
    public string UpdateDirectory { get; }
    public string StagingDirectory { get; }
    public string AccountsFile { get; }
    public string SettingsFile { get; }
    public string InstalledManifestFile { get; }
    public string TransactionFile { get; }
    public string LogsDirectory { get; }
    public string SecretsDirectory { get; }

    public string GetAbsoluteGamePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(MinecraftDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(MinecraftDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Relative path escaped game directory: {relativePath}");
        }

        return fullPath;
    }

    public void EnsureCreated()
    {
        foreach (var path in new[]
        {
            RootDirectory,
            MinecraftDirectory,
            RuntimeDirectory,
            VersionsDirectory,
            LibrariesDirectory,
            AssetsDirectory,
            ModsDirectory,
            UpdateDirectory,
            LogsDirectory,
            SecretsDirectory
        })
        {
            Directory.CreateDirectory(path);
        }
    }
}
