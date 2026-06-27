using System.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Services.Logging;

namespace FireflyMC.Launcher.Infrastructure.Process;

public sealed class GameProcess(IGameLogService logService, IDiagnosticLogger logger)
{
    public event EventHandler<int>? Exited;

    public System.Diagnostics.Process Start(LaunchProfile profile, bool redactIpAddresses)
    {
        var arguments = profile.JvmArguments
            .Concat(profile.LoggingArgument is null ? [] : new[] { profile.LoggingArgument })
            .Concat(new[] { "-cp", string.Join(Path.PathSeparator, profile.ClasspathEntries), profile.MainClass })
            .Concat(profile.GameArguments)
            .Select(QuoteIfNeeded);

        var startInfo = new ProcessStartInfo(profile.JavaExecutable)
        {
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        logger.LogInformation("启动 Minecraft 游戏进程");
        logService.Append(SecretRedactor.Redact($"启动命令: {profile.JavaExecutable} {string.Join(' ', startInfo.ArgumentList)}", redactIpAddresses));
        var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                logService.Append(SecretRedactor.Redact(e.Data, redactIpAddresses));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                logService.Append(SecretRedactor.Redact(e.Data, redactIpAddresses));
            }
        };
        process.Exited += (_, _) =>
        {
            logger.LogInformation($"游戏进程退出，退出码 {process.ExitCode}");
            Exited?.Invoke(this, process.ExitCode);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string QuoteIfNeeded(string value)
    {
        return value;
    }
}
