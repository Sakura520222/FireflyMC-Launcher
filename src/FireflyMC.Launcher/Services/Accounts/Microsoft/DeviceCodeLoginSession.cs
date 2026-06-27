using FireflyMC.Launcher.Contracts.Authentication.Microsoft;

namespace FireflyMC.Launcher.Services.Accounts.Microsoft;

public sealed class DeviceCodeLoginSession(DeviceCodeResponse deviceCode) : IDeviceCodeLoginSession
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public DeviceCodeResponse DeviceCode { get; } = deviceCode;
    public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);
    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public void Cancel()
    {
        _cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}
