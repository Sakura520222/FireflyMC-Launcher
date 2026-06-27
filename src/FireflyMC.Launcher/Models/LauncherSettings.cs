namespace FireflyMC.Launcher.Models;

public sealed record LauncherSettings
{
    public bool AutomaticMemory { get; init; } = true;
    public int MinMemoryMb { get; init; } = 1024;
    public int MaxMemoryMb { get; init; } = 4096;
    public string AdditionalJvmArgs { get; init; } = "";
    public string? JavaPathOverride { get; init; }
    public bool UseMirror { get; init; } = true;
    public bool AutoJoinServer { get; init; } = true;
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public bool AutoCheckUpdates { get; init; } = true;
    public string? CurrentAccountId { get; init; }
    public bool MinimizeOnGameLaunch { get; init; } = true;
    public bool RestoreAfterGameExit { get; init; } = true;
    public bool RecordNetworkDiagnostics { get; init; }
    public bool ShowUsernameAndUuidInLogs { get; init; } = true;
}
