namespace FireflyMC.Launcher.Models;

public enum UpdatePhase
{
    Idle,
    Resolving,
    Planning,
    Staging,
    Verifying,
    Committing,
    Cleanup
}

public sealed record TransactionState(
    Guid TransactionId,
    UpdatePhase Phase,
    string TargetManifestSha256,
    string? BackupPath,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> StagedFiles,
    IReadOnlyList<string> ReplacedFiles,
    IReadOnlyList<string> DeletedFiles);
