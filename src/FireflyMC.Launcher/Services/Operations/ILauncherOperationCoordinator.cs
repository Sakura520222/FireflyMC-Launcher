using FireflyMC.Launcher.Models.Operations;

namespace FireflyMC.Launcher.Services.Operations;

public interface ILauncherOperationCoordinator
{
    LauncherOperationState State { get; }
    bool IsBusy { get; }
    bool CanCancel { get; }
    event EventHandler? StateChanged;
    Task<IDisposable> BeginAsync(LauncherOperationState state, bool canCancel, CancellationToken cancellationToken);
    void SetState(LauncherOperationState state, bool canCancel);
    void Fail(Exception exception);
    void Cancel();
    CancellationToken CurrentCancellationToken { get; }
}
