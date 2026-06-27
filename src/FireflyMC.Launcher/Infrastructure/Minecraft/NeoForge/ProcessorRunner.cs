using System.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;

namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class ProcessorRunner
{
    private readonly IDiagnosticLogger _logger;

    public ProcessorRunner(IDiagnosticLogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(string fileName, string arguments, string workingDirectory, IProgress<string>? log, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"执行外部进程: {Path.GetFileName(fileName)}");
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log?.Report(SecretRedactor.Redact(e.Data));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log?.Report(SecretRedactor.Redact(e.Data));
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            _logger.LogError($"进程退出码 {process.ExitCode}: {fileName} {arguments}");
            throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}: {fileName} {arguments}");
        }

        _logger.LogDebug($"进程正常退出: {Path.GetFileName(fileName)}");
    }
}
