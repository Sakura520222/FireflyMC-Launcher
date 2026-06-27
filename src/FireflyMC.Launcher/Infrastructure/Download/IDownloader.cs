using FireflyMC.Launcher.Models;

namespace FireflyMC.Launcher.Infrastructure.Download;

public interface IDownloader
{
    Task DownloadAsync(
        Uri uri,
        string destinationPath,
        bool resume,
        IProgress<StageProgress>? progress,
        CancellationToken cancellationToken);
}
