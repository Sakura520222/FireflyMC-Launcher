using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace FireflyMC.Launcher.Infrastructure.Windows;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstanceService(string name)
    {
        _name = name;
        _mutex = new Mutex(initiallyOwned: true, name: $@"Global\{name}", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    public bool StartOrSignal(Action activate)
    {
        if (!IsFirstInstance)
        {
            SignalExistingInstance();
            return false;
        }

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
        catch
        {
        }
    }
}
