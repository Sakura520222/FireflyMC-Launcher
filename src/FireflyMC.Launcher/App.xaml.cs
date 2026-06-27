using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using FireflyMC.Launcher.Configuration;
using FireflyMC.Launcher.Infrastructure.Authentication.Microsoft;
using FireflyMC.Launcher.Infrastructure.Crypto;
using FireflyMC.Launcher.Infrastructure.Diagnostics;
using FireflyMC.Launcher.Infrastructure.Download;
using FireflyMC.Launcher.Infrastructure.Minecraft;
using FireflyMC.Launcher.Infrastructure.Minecraft.NeoForge;
using FireflyMC.Launcher.Infrastructure.Platforms;
using FireflyMC.Launcher.Infrastructure.Process;
using FireflyMC.Launcher.Infrastructure.Storage;
using FireflyMC.Launcher.Infrastructure.Windows;
using FireflyMC.Launcher.Services.Accounts;
using FireflyMC.Launcher.Services.Accounts.Microsoft;
using FireflyMC.Launcher.Services.Accounts.Offline;
using FireflyMC.Launcher.Services.Dialogs;
using FireflyMC.Launcher.Services.Install;
using FireflyMC.Launcher.Services.Launch;
using FireflyMC.Launcher.Services.Logging;
using FireflyMC.Launcher.Services.Navigation;
using FireflyMC.Launcher.Services.Operations;
using FireflyMC.Launcher.Services.SelfUpdate;
using FireflyMC.Launcher.Services.Update;
using FireflyMC.Launcher.ViewModels;
using FireflyMC.Launcher.Views;

namespace FireflyMC.Launcher;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private SingleInstanceService? _singleInstance;
    private IDiagnosticLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _serviceProvider = ConfigureServices();
        _logger = _serviceProvider.GetRequiredService<IDiagnosticLogger>();
        RegisterExceptionHandlers();
        var paths = _serviceProvider.GetRequiredService<ILauncherPaths>();
        paths.EnsureCreated();
        _ = ApplyNetworkDiagnosticsSettingAsync();
        _logger.LogInformation("启动器启动");
        ConfirmUpdateSuccess(paths, e.Args);

        _singleInstance = new SingleInstanceService("FireflyMCLauncher", _logger);
        if (!_singleInstance.StartOrSignal(ActivateMainWindow))
        {
            _logger.LogInformation("已有实例运行，退出当前进程");
            Shutdown();
            return;
        }

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        _logger.LogInformation("主窗口已显示");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation($"启动器退出（退出码 {e.ApplicationExitCode}）");
        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var configuration = LauncherConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var userAgent = LauncherUserAgent.Create(configuration);
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(userAgent);
        services.AddSingleton(configuration.MicrosoftAuth);
        services.AddSingleton(configuration.CurseForge);
        services.AddSingleton(configuration.Update);
        services.AddSingleton(_ =>
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent.Value);
            return httpClient;
        });
        services.AddSingleton<ILauncherPaths, LauncherPaths>();
        services.AddSingleton<IDiagnosticLogger>(sp =>
        {
            var launcherPaths = sp.GetRequiredService<ILauncherPaths>();
            return new FileDiagnosticLogger(launcherPaths.LogsDirectory);
        });
        services.AddSingleton<IAccountStore, JsonAccountStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISecretStore, WindowsSecretStore>();
        services.AddSingleton<IInstalledManifestStore, JsonInstalledManifestStore>();
        services.AddSingleton<IHashVerifier, HashVerifier>();
        services.AddSingleton<IDownloader, HttpDownloader>();
        services.AddSingleton<MirrorRouter>();
        services.AddSingleton<ModrinthClient>();
        services.AddSingleton<CurseForgeClient>();
        services.AddSingleton<ModPlatformResolver>();
        services.AddSingleton<IMicrosoftOAuthClient, MicrosoftOAuthClient>();
        services.AddSingleton<IXboxLiveClient, XboxLiveClient>();
        services.AddSingleton<IMinecraftServicesClient, MinecraftServicesClient>();
        services.AddSingleton<IMicrosoftAuthService, MicrosoftAuthService>();
        services.AddSingleton<OfflineUuidProvider>();
        services.AddSingleton<IOfflineAccountService, OfflineAccountService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<IUpdateTransaction, UpdateTransaction>();
        services.AddSingleton<IModPackUpdateService, ModPackUpdateService>();
        services.AddSingleton<AdoptiumJavaRuntimeInstaller>();
        services.AddSingleton<McVersionInstaller>();
        services.AddSingleton<InstallProfileReader>();
        services.AddSingleton<MavenArtifactResolver>();
        services.AddSingleton<ProcessorRunner>();
        services.AddSingleton<VersionJsonMerger>();
        services.AddSingleton<NeoForgeClientInstaller>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<IGameLogService, GameLogService>();
        services.AddSingleton<GameProcess>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
        services.AddSingleton<ILauncherOperationCoordinator, LauncherOperationCoordinator>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDeviceCodeDialogService, DeviceCodeDialogService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<DownloadViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
    }

    private static void ConfirmUpdateSuccess(ILauncherPaths paths, string[] args)
    {
        var index = Array.IndexOf(args, "--update-success");
        if (index >= 0 && index + 1 < args.Length)
        {
            Directory.CreateDirectory(paths.UpdateDirectory);
            File.WriteAllText(Path.Combine(paths.UpdateDirectory, $"success-{args[index + 1]}"), DateTimeOffset.UtcNow.ToString("O"));
        }
    }

    private async Task ApplyNetworkDiagnosticsSettingAsync()
    {
        try
        {
            var store = _serviceProvider!.GetRequiredService<ISettingsStore>();
            var settings = await store.LoadAsync(CancellationToken.None);
            _logger!.RecordNetworkDiagnostics = settings.RecordNetworkDiagnostics;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("加载网络诊断设置失败，使用默认值", ex);
        }
    }

    private void RegisterExceptionHandlers()
    {
        Current.DispatcherUnhandledException += (_, e) =>
        {
            _logger?.LogError("未处理的 UI 异常", e.Exception);
            MessageBox.Show(e.Exception.Message, "FireflyMC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (_logger is null)
            {
                TryWriteCrashFallback("last-crash.txt", e.ExceptionObject?.ToString() ?? string.Empty);
                return;
            }

            _logger.LogCritical("AppDomain 未处理异常", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogError("未观察的 Task 异常", e.Exception);
            e.SetObserved();
        };
    }

    private static void TryWriteCrashFallback(string fileName, string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, fileName), content);
        }
        catch
        {
            // 兜底写入失败时无可观测手段，忽略以避免在异常路径上再抛。
        }
    }
}
