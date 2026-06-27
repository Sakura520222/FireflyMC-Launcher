using System.Text;
using FireflyMC.Launcher.Infrastructure.Crypto;

namespace FireflyMC.Launcher.Infrastructure.Diagnostics;

/// <summary>
/// 应用诊断日志级别。游戏进程输出走 <c>IGameLogService</c>，与此处的应用日志区分。
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// 应用诊断日志器。落盘到 <c>&lt;root&gt;/logs/launcher.log</c>，统一经 <see cref="SecretRedactor"/> 脱敏（spec §7.9 永远开启）。
/// </summary>
public interface IDiagnosticLogger
{
    LogLevel MinimumLevel { get; set; }

    /// <summary>是否记录网络诊断细节（Debug/Trace 级）。由 <c>LauncherSettings.RecordNetworkDiagnostics</c> 驱动。</summary>
    bool RecordNetworkDiagnostics { get; set; }

    bool IsEnabled(LogLevel level);

    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class DiagnosticLoggerExtensions
{
    public static void LogTrace(this IDiagnosticLogger logger, string message) => logger.Log(LogLevel.Trace, message);

    public static void LogDebug(this IDiagnosticLogger logger, string message, Exception? exception = null) => logger.Log(LogLevel.Debug, message, exception);

    public static void LogInformation(this IDiagnosticLogger logger, string message) => logger.Log(LogLevel.Information, message);

    public static void LogWarning(this IDiagnosticLogger logger, string message, Exception? exception = null) => logger.Log(LogLevel.Warning, message, exception);

    public static void LogError(this IDiagnosticLogger logger, string message, Exception? exception = null) => logger.Log(LogLevel.Error, message, exception);

    public static void LogCritical(this IDiagnosticLogger logger, string message, Exception? exception = null) => logger.Log(LogLevel.Critical, message, exception);
}

/// <summary>
/// 文件诊断日志器。线程安全，按 10MB 单备份轮转。写入与轮转的 IO 异常被吞掉——
/// 诊断日志自身的故障绝不能拖垮宿主流程（spec §7.10 要求异常分级记录后安全退出，日志缺失不在此列）。
/// </summary>
public sealed class FileDiagnosticLogger : IDiagnosticLogger
{
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private readonly string _logPath;
    private readonly string _previousLogPath;
    private readonly object _gate = new();

    public FileDiagnosticLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "launcher.log");
        _previousLogPath = Path.Combine(logDirectory, "launcher.1.log");
        MinimumLevel = LogLevel.Information;
    }

    public LogLevel MinimumLevel { get; set; }

    public bool RecordNetworkDiagnostics { get; set; }

    public bool IsEnabled(LogLevel level)
    {
        if (level >= MinimumLevel)
        {
            return true;
        }

        // Debug/Trace 保留给网络/解析细节，受用户开关控制（默认关，对应设置页"记录网络诊断信息"）。
        return level is LogLevel.Debug or LogLevel.Trace && RecordNetworkDiagnostics;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var sanitized = SecretRedactor.Redact(message, redactIpAddresses: true);
        var builder = new StringBuilder($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {sanitized}");
        if (exception is not null)
        {
            builder.Append('\n').Append(SecretRedactor.Redact(exception.ToString(), redactIpAddresses: true));
        }

        builder.Append('\n');

        lock (_gate)
        {
            RollIfNeeded();
            File.AppendAllText(_logPath, builder.ToString(), Encoding.UTF8);
        }
    }

    private void RollIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < MaxFileBytes)
            {
                return;
            }

            if (File.Exists(_previousLogPath))
            {
                File.Delete(_previousLogPath);
            }

            File.Move(_logPath, _previousLogPath);
        }
        catch
        {
            // 轮转失败不影响后续追加写入。
        }
    }
}

/// <summary>
/// 空实现诊断日志器，供测试与不需要落盘日志的场景注入。
/// </summary>
public sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Critical;

    public bool RecordNetworkDiagnostics { get; set; }

    public bool IsEnabled(LogLevel level) => false;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }
}
