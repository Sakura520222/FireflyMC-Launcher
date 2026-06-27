using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using FireflyMC.Launcher.Infrastructure.Diagnostics;

namespace FireflyMC.Launcher.Infrastructure.Windows;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private readonly IDiagnosticLogger _logger;

    public SingleInstanceService(string name, IDiagnosticLogger logger)
    {
        _name = name;
        _logger = logger;
        _mutex = new Mutex(initiallyOwned: true, name: $@"Global\{name}", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public bool StartOrSignal(Action activate)
    {
        if (!IsFirstInstance)
        {
            _logger.LogInformation("检测到已有实例运行，发送激活信号后退出");
            SignalExistingInstance();
            return false;
        }

        _logger.LogInformation("以首个实例启动，开始监听激活请求");
        _ = ListenAsync(activate, _cts.Token);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private async Task ListenAsync(Action activate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                _ = await reader.ReadLineAsync(cancellationToken);
                Application.Current.Dispatcher.Invoke(activate);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 监听循环中的瞬时错误忽略后继续等待下一次连接，避免刷屏。
            }
        }
    }

    private void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("activate");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("向已有实例发送激活信号失败", ex);
        }
    }
}
