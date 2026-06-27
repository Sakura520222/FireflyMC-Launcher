namespace FireflyMC.Launcher.Models;

public enum OperationStage
{
    Idle,
    Resolve,
    Plan,
    Java,
    Minecraft,
    NeoForge,
    Stage,
    Verify,
    Commit,
    Cleanup,
    Launch,
    SelfUpdate
}

public sealed record StageProgress(
    OperationStage Stage,
    double? StagePercent,
    double? OverallPercent,
    string? CurrentItem,
    long CompletedBytes,
    long? TotalBytes,
    double? BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    bool CanCancel);
