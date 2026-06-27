using System.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Crypto;

namespace FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;

public sealed class ProcessorRunner
{
    public async Task RunAsync(string fileName, string arguments, string workingDirectory, IProgress<string>? log, CancellationToken cancellationToken)
    {
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
            throw new InvalidOperationException($"Process failed with exit code {process.ExitCode}: {fileName} {arguments}");
        }
    }
}
