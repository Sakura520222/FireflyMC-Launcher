using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Download;

public sealed class HttpDownloader(HttpClient httpClient, UpdateOptions options, IDiagnosticLogger logger) : IDownloader
{
    public async Task DownloadAsync(
        Uri uri,
        string destinationPath,
        bool resume,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partPath = $"{destinationPath}.part";
        Exception? last = null;
        logger.LogDebug($"下载 {uri} -> {Path.GetFileName(destinationPath)}");

        for (var attempt = 0; attempt <= options.MaxRetries; attempt++)
        {
            try
            {
                await DownloadOnceAsync(uri, partPath, resume, progress, cancellationToken);
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(partPath, destinationPath);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < options.MaxRetries)
            {
                last = ex;
                logger.LogWarning($"下载失败（第 {attempt + 1}/{options.MaxRetries + 1} 次），{options.RetryBaseDelaySeconds * Math.Pow(2, attempt)}s 后重试: {uri}", ex);
                var delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
        }

        logger.LogError($"下载在 {options.MaxRetries + 1} 次尝试后仍失败: {uri}", last);
        throw new IOException($"Download failed after {options.MaxRetries + 1} attempts: {uri}", last);
    }

    private async Task DownloadOnceAsync(
        Uri uri,
        string partPath,
        bool resume,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingLength = resume && File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existingLength > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.PerFileTimeoutSeconds));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        if (existingLength > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength is { } length ? length + existingLength : null;
        await using var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        await using var target = new FileStream(
            partPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        var completed = existingLength;
        var lastReport = DateTimeOffset.UtcNow;
        var lastBytes = completed;
        while (true)
        {
            var read = await source.ReadAsync(buffer, timeoutCts.Token);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token);
            completed += read;
            var now = DateTimeOffset.UtcNow;
            if ((now - lastReport).TotalMilliseconds >= 250)
            {
                var speed = (completed - lastBytes) / Math.Max(0.001, (now - lastReport).TotalSeconds);
                TimeSpan? remaining = total is { } totalBytes && speed > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, (totalBytes - completed) / speed))
                    : null;
                progress?.Report(new StageProgress(
                    OperationStage.Stage,
                    total is { } t ? completed * 100d / t : null,
                    null,
                    Path.GetFileName(partPath),
                    completed,
                    total,
                    speed,
                    remaining,
                    true));
                lastReport = now;
                lastBytes = completed;
            }
        }
    }
}
