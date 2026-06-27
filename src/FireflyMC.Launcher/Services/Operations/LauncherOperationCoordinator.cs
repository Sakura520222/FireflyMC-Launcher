using FireflyMC.Launcher.Models.Operations;

namespace FireflyMC.Launcher.Services.Operations;

public sealed class LauncherOperationCoordinator : ILauncherOperationCoordinator
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private CancellationTokenSource? _currentCts;

    public LauncherOperationState State { get; private set; } = LauncherOperationState.Idle;
    public bool IsBusy => State != LauncherOperationState.Idle && State != LauncherOperationState.Failed;
    public bool CanCancel { get; private set; }
    public CancellationToken CurrentCancellationToken => _currentCts?.Token ?? CancellationToken.None;
    public event EventHandler? StateChanged;

    public async Task<IDisposable> BeginAsync(LauncherOperationState state, bool canCancel, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetState(state, canCancel);
        return new Releaser(this);
    }

    public void SetState(LauncherOperationState state, bool canCancel)
    {
        State = state;
        CanCancel = canCancel;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        if (CanCancel)
        {
            _currentCts?.Cancel();
        }
    }

    private void Release()
    {
        _currentCts?.Dispose();
        _currentCts = null;
        State = LauncherOperationState.Idle;
        CanCancel = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _operationLock.Release();
    }

    private sealed class Releaser(LauncherOperationCoordinator coordinator) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            coordinator.Release();
        }
    }
}
