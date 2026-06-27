using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Accounts;

namespace FireflyMC.Launcher.Services.Launch;

public interface ILaunchService
{
    Task<LaunchProfile> BuildLaunchProfileAsync(AccountProfile account, CancellationToken cancellationToken);
    Task LaunchAsync(AccountProfile account, CancellationToken cancellationToken);
}
