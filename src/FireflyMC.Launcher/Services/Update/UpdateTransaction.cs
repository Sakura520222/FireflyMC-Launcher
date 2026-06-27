using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Services.Update;

public sealed class UpdateTransaction(ILauncherPaths paths, IDiagnosticLogger logger) : IUpdateTransaction
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var state = await JsonFile.ReadAsync(paths.TransactionFile, JsonContext.Default.TransactionState, cancellationToken);
        if (state is null)
        {
            logger.LogInformation("未发现未完成事务，清理残留 .part 文件");
            DeletePartFiles(paths.StagingDirectory);
            return;
        }

        logger.LogWarning($"发现未完成事务 {state.TransactionId:N}，阶段={state.Phase}，开始恢复");
        switch (state.Phase)
        {
            case UpdatePhase.Resolving:
            case UpdatePhase.Planning:
                DeleteFileIfExists(paths.TransactionFile);
                break;
            case UpdatePhase.Staging:
            case UpdatePhase.Verifying:
                DeleteDirectoryIfExists(paths.StagingDirectory);
                DeleteFileIfExists(paths.TransactionFile);
                break;
            case UpdatePhase.Committing:
                await RollbackAsync(state, cancellationToken);
                break;
            case UpdatePhase.Cleanup:
                await CleanupAsync(state.TransactionId, state.BackupPath, cancellationToken);
                break;
            default:
                DeleteFileIfExists(paths.TransactionFile);
                break;
        }

        logger.LogInformation("事务恢复完成");
    }

    public async Task ExecuteAsync(
        UpdatePlan plan,
        Func<string, string> stagePathSelector,
        Func<Task> saveInstalledManifest,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken)
    {
        var transactionId = Guid.NewGuid();
        var state = new TransactionState(transactionId, UpdatePhase.Committing, plan.TargetManifestSha256, null, DateTimeOffset.UtcNow, [], [], []);
        var backupPath = Path.Combine(paths.UpdateDirectory, $"backup-{transactionId:N}");
        Directory.CreateDirectory(backupPath);
        state = state with { BackupPath = backupPath };
        await WriteStateAsync(state, cancellationToken);
        logger.LogInformation($"开始提交事务 {transactionId:N}（{plan.Downloads.Count} 替换，{plan.Deletes.Count} 删除）");

        try
        {
            var replaced = new List<string>();
            var deleted = new List<string>();

            foreach (var download in plan.Downloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = paths.GetAbsoluteGamePath(download.RelativePath);
                var staged = stagePathSelector(download.RelativePath);
                if (File.Exists(destination))
                {
                    var backup = GetBackupPath(backupPath, download.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                    replaced.Add(download.RelativePath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(staged, destination, overwrite: true);
                state = state with { ReplacedFiles = replaced.ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
                await WriteStateAsync(state, cancellationToken);
            }

            foreach (var delete in plan.Deletes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = paths.GetAbsoluteGamePath(delete.RelativePath);
                if (!File.Exists(target))
                {
                    continue;
                }

                var backup = GetBackupPath(backupPath, delete.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(target, backup, overwrite: true);
                deleted.Add(delete.RelativePath);
                state = state with { DeletedFiles = deleted.ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
                await WriteStateAsync(state, cancellationToken);
            }

            await saveInstalledManifest();
            state = state with { Phase = UpdatePhase.Cleanup, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteStateAsync(state, cancellationToken);
            await CleanupAsync(transactionId, backupPath, cancellationToken);
            progress?.Report(new StageProgress(OperationStage.Cleanup, 100, 100, "更新完成", 0, null, null, null, false));
            logger.LogInformation($"事务 {transactionId:N} 提交完成");
        }
        catch (Exception ex)
        {
            logger.LogError($"事务 {transactionId:N} 提交失败，执行回滚", ex);
            await RollbackAsync(state, CancellationToken.None);
            throw;
        }
    }

    private async Task RollbackAsync(TransactionState state, CancellationToken cancellationToken)
    {
        logger.LogWarning($"从事务备份回滚：{state.ReplacedFiles.Count} 替换，{state.DeletedFiles.Count} 删除");
        if (!string.IsNullOrWhiteSpace(state.BackupPath) && Directory.Exists(state.BackupPath))
        {
            foreach (var relativePath in state.ReplacedFiles.Concat(state.DeletedFiles).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var backup = GetBackupPath(state.BackupPath, relativePath);
                var target = paths.GetAbsoluteGamePath(relativePath);
                if (!File.Exists(backup))
                {
                    if (state.ReplacedFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase) && File.Exists(target))
                    {
                        File.Delete(target);
                    }

                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(backup, target, overwrite: true);
            }
        }

        DeleteDirectoryIfExists(paths.StagingDirectory);
        DeleteDirectoryIfExists(state.BackupPath);
        DeleteFileIfExists(paths.TransactionFile);
        await Task.CompletedTask.WaitAsync(cancellationToken);
    }

    private async Task CleanupAsync(Guid transactionId, string? backupPath, CancellationToken cancellationToken)
    {
        logger.LogDebug($"清理事务 {transactionId:N} 的 staging 与历史备份");
        DeleteDirectoryIfExists(paths.StagingDirectory);
        var retainedRoot = Path.Combine(paths.UpdateDirectory, "retained");
        Directory.CreateDirectory(retainedRoot);
        foreach (var retained in Directory.EnumerateDirectories(retainedRoot))
        {
            DeleteDirectoryIfExists(retained);
        }

        if (!string.IsNullOrWhiteSpace(backupPath) && Directory.Exists(backupPath))
        {
            Directory.Move(backupPath, Path.Combine(retainedRoot, transactionId.ToString("N")));
        }

        DeleteFileIfExists(paths.TransactionFile);
        await Task.CompletedTask.WaitAsync(cancellationToken);
    }

    private Task WriteStateAsync(TransactionState state, CancellationToken cancellationToken)
    {
        return JsonFile.WriteAtomicAsync(paths.TransactionFile, state, JsonContext.Default.TransactionState, cancellationToken);
    }

    private static string GetBackupPath(string backupRoot, string relativePath)
    {
        return Path.Combine(backupRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void DeletePartFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories))
        {
            DeleteFileIfExists(path);
        }
    }

    private static void DeleteDirectoryIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
