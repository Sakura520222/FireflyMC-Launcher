using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models.Operations;

namespace FireflyMC.Launcher.Services.Operations;

public sealed class LauncherOperationCoordinator : ILauncherOperationCoordinator
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly IDiagnosticLogger _logger;
    private CancellationTokenSource? _currentCts;

    public LauncherOperationCoordinator(IDiagnosticLogger logger)
    {
        _logger = logger;
    }

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
        _logger.LogInformation($"操作状态变更: {state}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        if (CanCancel)
        {
            _logger.LogInformation("请求取消当前操作");
            _currentCts?.Cancel();
        }
    }

    /// 操作失败：记录异常详情到诊断日志后置 <see cref="LauncherOperationState.Failed"/>。
    /// 调用方 catch 异常时应走这里，避免失败原因只进 UI 不进日志。
    public void Fail(Exception exception)
    {
        _logger.LogError("操作失败", exception);
        SetState(LauncherOperationState.Failed, canCancel: false);
    }

    private void Release()
    {
        _logger.LogInformation($"操作结束，回到 {nameof(LauncherOperationState.Idle)}");
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
