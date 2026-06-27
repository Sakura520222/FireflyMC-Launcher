using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Services.Accounts.Microsoft;

public interface IDeviceCodeLoginSession : IDisposable
{
    DeviceCodeResponse DeviceCode { get; }
    DateTimeOffset ExpiresAt { get; }
    CancellationToken CancellationToken { get; }
    void Cancel();
}
