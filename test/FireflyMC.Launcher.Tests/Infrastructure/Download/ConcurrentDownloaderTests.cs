using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Models;
using FluentAssertions;

namespace FireflyMC.Launcher.Tests.Infrastructure.Download;

public sealed class ConcurrentDownloaderTests
{
    [Fact]
    public async Task DownloadAllAsync_AllSucceed_ReturnsAllFilesWithActualSha1()
    {
        var root = CreateTempRoot();
        var downloader = new FakeDownloader();
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = new[]
        {
            Job(new Uri("https://test/a"), Path.Combine(root, "a")),
            Job(new Uri("https://test/b"), Path.Combine(root, "b")),
            Job(new Uri("https://test/c"), Path.Combine(root, "c"))
        };

        var result = await service.DownloadAllAsync(jobs, maxConcurrency: 2, OperationStage.Stage, null, null, null, CancellationToken.None);

        result.Files.Should().HaveCount(3);
        result.SkippedCount.Should().Be(0);
        result.Files["a"].ActualSha1.Should().NotBeNullOrEmpty();
        result.Files["b"].ActualSha1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadAllAsync_RequiredDownloadFailure_Throws()
    {
        var root = CreateTempRoot();
        var failing = new Uri("https://test/fail");
        var downloader = new FakeDownloader(failures: new() { [failing] = new IOException("boom") });
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = new[]
        {
            Job(failing, Path.Combine(root, "fail"), required: true),
            Job(new Uri("https://test/ok"), Path.Combine(root, "ok"), required: true)
        };

        var act = () => service.DownloadAllAsync(jobs, maxConcurrency: 2, OperationStage.Stage, null, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task DownloadAllAsync_RequiredSha1Mismatch_ThrowsInvalidData()
    {
        var root = CreateTempRoot();
        var downloader = new FakeDownloader();
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = new[]
        {
            Job(new Uri("https://test/a"), Path.Combine(root, "a"), required: true, expectedSha1: "deadbeef")
        };

        var act = () => service.DownloadAllAsync(jobs, maxConcurrency: 2, OperationStage.Stage, null, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DownloadAllAsync_OptionalFailure_SkipsAndContinues()
    {
        var root = CreateTempRoot();
        var failing = new Uri("https://test/optional");
        var downloader = new FakeDownloader(failures: new() { [failing] = new IOException("boom") });
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = new[]
        {
            Job(failing, Path.Combine(root, "optional"), required: false),
            Job(new Uri("https://test/ok"), Path.Combine(root, "ok"), required: true)
        };

        var result = await service.DownloadAllAsync(jobs, maxConcurrency: 2, OperationStage.Stage, null, null, null, CancellationToken.None);

        result.Files.Should().ContainSingle().Which.Key.Should().Be("ok");
        result.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task DownloadAllAsync_RespectsMaxConcurrency()
    {
        var root = CreateTempRoot();
        var downloader = new FakeDownloader(delay: TimeSpan.FromMilliseconds(100));
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = Enumerable.Range(0, 12)
            .Select(i => Job(new Uri($"https://test/{i}"), Path.Combine(root, i.ToString())))
            .ToArray();

        await service.DownloadAllAsync(jobs, maxConcurrency: 4, OperationStage.Stage, null, null, null, CancellationToken.None);

        Assert.InRange(downloader.MaxObservedConcurrency, 2, 4);
    }

    [Fact]
    public async Task DownloadAllAsync_ExternalCancellation_ThrowsOperationCanceled()
    {
        var root = CreateTempRoot();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var downloader = new FakeDownloader(delay: TimeSpan.FromMilliseconds(500));
        var service = new ConcurrentDownloader(downloader, new HashVerifier(), new NullDiagnosticLogger());
        var jobs = new[]
        {
            Job(new Uri("https://test/a"), Path.Combine(root, "a"), required: true),
            Job(new Uri("https://test/b"), Path.Combine(root, "b"), required: true)
        };

        var act = () => service.DownloadAllAsync(jobs, maxConcurrency: 2, OperationStage.Stage, null, null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ConcurrentDownloadJob Job(Uri uri, string destination, bool required = true, string? expectedSha1 = null, bool recordActual = true)
    {
        return new ConcurrentDownloadJob(uri, destination, Path.GetFileName(destination), required, expectedSha1, recordActual);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"concurrent-downloader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    /// 可控的 <see cref="IDownloader"/>：记录观察到的最大并发数，按 Uri 注入失败，可选延迟。
    private sealed class FakeDownloader : IDownloader
    {
        private readonly Dictionary<Uri, Exception> _failures;
        private readonly TimeSpan _delay;
        private int _current;
        private int _maxObserved;

        public FakeDownloader(Dictionary<Uri, Exception>? failures = null, TimeSpan delay = default)
        {
            _failures = failures ?? new Dictionary<Uri, Exception>();
            _delay = delay;
        }

        public int MaxObservedConcurrency => _maxObserved;

        public async Task DownloadAsync(Uri uri, string destinationPath, bool resume, IProgress<StageProgress>? progress, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            TrackMax(current);
            try
            {
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_failures.TryGetValue(uri, out var ex))
                {
                    throw ex;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllTextAsync(destinationPath, $"content-{uri}", cancellationToken);
                progress?.Report(new StageProgress(OperationStage.Stage, 100, null, uri.ToString(), 8, 8, null, null, true));
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        private void TrackMax(int current)
        {
            int observed;
            do
            {
                observed = _maxObserved;
                if (current <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxObserved, current, observed) != observed);
        }
    }
}
