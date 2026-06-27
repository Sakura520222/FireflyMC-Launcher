using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Download;

/// 单个并发下载任务。Label 用作进度展示与结果索引（mod=相对路径，assets=hash/文件名）。
public sealed record ConcurrentDownloadJob(
    Uri Uri,
    string DestinationPath,
    string Label,
    bool Required,
    string? ExpectedSha1,
    bool RecordActualSha1);

public sealed record DownloadedFile(string Path, string? ActualSha1);

public sealed record ConcurrentDownloadResult(
    IReadOnlyDictionary<string, DownloadedFile> Files,
    int SkippedCount);

/// 文件间并发下载器。用 <see cref="SemaphoreSlim"/> 限流，复用 <see cref="IDownloader"/> 的单文件续传/重试；
/// 字节级进度聚合，required 失败 fast-fail（取消在途任务），可选失败跳过。单文件分段不在范围。
public sealed class ConcurrentDownloader
{
    private readonly IDownloader _downloader;
    private readonly IHashVerifier _hashVerifier;
    private readonly IDiagnosticLogger _logger;

    public ConcurrentDownloader(IDownloader downloader, IHashVerifier hashVerifier, IDiagnosticLogger logger)
    {
        _downloader = downloader;
        _hashVerifier = hashVerifier;
        _logger = logger;
    }

    public async Task<ConcurrentDownloadResult> DownloadAllAsync(
        IReadOnlyList<ConcurrentDownloadJob> jobs,
        int maxConcurrency,
        OperationStage stage,
        double? overallBase,
        double? overallSpan,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (jobs.Count == 0)
        {
            return new ConcurrentDownloadResult(new Dictionary<string, DownloadedFile>(), 0);
        }

        var concurrency = Math.Max(1, maxConcurrency);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var semaphore = new SemaphoreSlim(concurrency, concurrency);
        var succeeded = new ConcurrentDictionary<string, DownloadedFile>();
        var errors = new ConcurrentQueue<Exception>();
        var skipped = new StrongBox<int>(0);
        var aggregator = new ProgressAggregator(progress, stage, overallBase, overallSpan, jobs.Count);

        var tasks = jobs.Select(async job =>
        {
            var acquired = false;
            try
            {
                await semaphore.WaitAsync(linkedCts.Token);
                acquired = true;
                var jobProgress = new ActionProgress<StageProgress>(sp => aggregator.Update(job.Label, sp.CompletedBytes, sp.TotalBytes));
                await _downloader.DownloadAsync(job.Uri, job.DestinationPath, resume: true, jobProgress, linkedCts.Token);

                if (job.ExpectedSha1 is { } expected
                    && !await _hashVerifier.VerifySha1Async(job.DestinationPath, expected, linkedCts.Token))
                {
                    if (job.Required)
                    {
                        _logger.LogError($"必需文件 SHA-1 校验失败: {job.Label}");
                        errors.Enqueue(new InvalidDataException($"SHA-1 mismatch for required file {job.Label}."));
                        linkedCts.Cancel();
                        return;
                    }

                    _logger.LogWarning($"可选文件 SHA-1 校验失败，跳过: {job.Label}");
                    Interlocked.Increment(ref skipped.Value);
                    return;
                }

                string? actualSha1 = null;
                if (job.RecordActualSha1)
                {
                    actualSha1 = await _hashVerifier.ComputeSha1Async(job.DestinationPath, linkedCts.Token);
                }

                succeeded[job.Label] = new DownloadedFile(job.DestinationPath, actualSha1);
                aggregator.IncrementCompleted();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 外部取消（用户取消操作），静默——由方法末尾 ThrowIfCancellationRequested 抛出。
            }
            catch (OperationCanceledException)
            {
                // linkedCts 因某 required 失败而取消，静默——错误已入队。
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (job.Required)
                {
                    _logger.LogError($"必需文件下载失败: {job.Label}", ex);
                    errors.Enqueue(ex);
                    linkedCts.Cancel();
                }
                else
                {
                    _logger.LogWarning($"可选文件下载失败，跳过: {job.Label}", ex);
                    Interlocked.Increment(ref skipped.Value);
                }
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        cancellationToken.ThrowIfCancellationRequested();
        if (errors.TryDequeue(out var firstError))
        {
            throw firstError;
        }

        _logger.LogInformation($"并发下载完成：{succeeded.Count} 成功，{skipped.Value} 跳过（共 {jobs.Count}）");
        return new ConcurrentDownloadResult(succeeded.ToDictionary(), skipped.Value);
    }

    /// 轻量 <see cref="IProgress{T}"/>，直接在回调线程触发（不用 <c>Progress&lt;T&gt;</c> 的 SynchronizationContext）。
    private sealed class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public ActionProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }

    /// 单次 <see cref="DownloadAllAsync"/> 调用的字节级进度聚合器（per-call，无实例共享状态）。
    private sealed class ProgressAggregator
    {
        private readonly IProgress<StageProgress>? _progress;
        private readonly OperationStage _stage;
        private readonly double? _overallBase;
        private readonly double? _overallSpan;
        private readonly int _totalJobs;
        private readonly object _gate = new();
        private readonly Dictionary<string, (long Completed, long? Total)> _perJob = new();
        private DateTimeOffset _lastReport = DateTimeOffset.UtcNow;
        private long _lastReportedCompleted;
        private int _completedFiles;

        public ProgressAggregator(IProgress<StageProgress>? progress, OperationStage stage, double? overallBase, double? overallSpan, int totalJobs)
        {
            _progress = progress;
            _stage = stage;
            _overallBase = overallBase;
            _overallSpan = overallSpan;
            _totalJobs = totalJobs;
        }

        public void Update(string label, long completed, long? total)
        {
            lock (_gate)
            {
                _perJob[label] = (completed, total);
                ReportIfDue();
            }
        }

        public void IncrementCompleted()
        {
            lock (_gate)
            {
                _completedFiles++;
                ReportIfDue();
            }
        }

        private void ReportIfDue()
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - _lastReport).TotalMilliseconds < 200)
            {
                return;
            }

            var previous = _lastReport;
            _lastReport = now;

            var completedBytes = _perJob.Values.Sum(v => v.Completed);
            var knownTotal = _perJob.Values.Where(v => v.Total is > 0).Sum(v => v.Total!.Value);

            double? stagePercent = knownTotal > 0
                ? Math.Min(100, completedBytes * 100d / knownTotal)
                : (_totalJobs > 0 ? _completedFiles * 100d / _totalJobs : null);

            double? overallPercent = (_overallBase is { } b && _overallSpan is { } s && stagePercent is { } sp)
                ? b + s * sp / 100d
                : null;

            var elapsedSeconds = Math.Max(0.001, (now - previous).TotalSeconds);
            var speed = completedBytes > _lastReportedCompleted
                ? (completedBytes - _lastReportedCompleted) / elapsedSeconds
                : 0;
            _lastReportedCompleted = completedBytes;

            TimeSpan? remaining = knownTotal > 0 && speed > 0
                ? TimeSpan.FromSeconds(Math.Max(0, (knownTotal - completedBytes) / speed))
                : null;

            _progress?.Report(new StageProgress(
                _stage,
                stagePercent,
                overallPercent,
                $"下载中 {_completedFiles}/{_totalJobs}",
                completedBytes,
                knownTotal > 0 ? knownTotal : null,
                speed,
                remaining,
                true));
        }
    }
}
