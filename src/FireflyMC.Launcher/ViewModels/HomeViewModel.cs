using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Models.Operations;
using FireflyMC.Launcher.Services.Accounts;
using FireflyMC.Launcher.Services.Install;
using FireflyMC.Launcher.Services.Launch;
using FireflyMC.Launcher.Services.Operations;

namespace FireflyMC.Launcher.ViewModels;

public sealed partial class HomeViewModel(
    IAccountService accountService,
    IInstallService installService,
    ILaunchService launchService,
    ILauncherOperationCoordinator coordinator) : PageViewModelBase("主页")
{
    [ObservableProperty]
    private AccountProfile? _selectedAccount;

    [ObservableProperty]
    private string _status = "未检查";

    [ObservableProperty]
    private string _mainButtonText = "启动游戏";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "";

    public ObservableCollection<AccountProfile> Accounts { get; } = [];

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RefreshAccountsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        Accounts.Clear();
        foreach (var account in await accountService.GetAccountsAsync(cancellationToken))
        {
            Accounts.Add(account);
        }

        SelectedAccount ??= Accounts.FirstOrDefault();
        Status = Accounts.Count == 0 ? "请先添加账号" : "实例就绪或可安装";
        MainButtonText = Accounts.Count == 0 ? "前往账号页" : "启动游戏";
    }

    [RelayCommand]
    private async Task MainActionAsync(CancellationToken cancellationToken)
    {
        if (SelectedAccount is null)
        {
            Status = "请先添加账号";
            return;
        }

        using var operation = await coordinator.BeginAsync(LauncherOperationState.PreparingLaunch, canCancel: true, cancellationToken);
        var progress = new Progress<StageProgress>(OnProgress);
        try
        {
            coordinator.SetState(LauncherOperationState.Installing, canCancel: true);
            await installService.InstallAsync(progress, coordinator.CurrentCancellationToken);
            coordinator.SetState(LauncherOperationState.Launching, canCancel: false);
            await launchService.LaunchAsync(SelectedAccount, coordinator.CurrentCancellationToken);
            Status = "已启动";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
        catch (Exception ex)
        {
            coordinator.Fail(ex);
            Status = ex.Message;
        }
    }

    private void OnProgress(StageProgress progress)
    {
        ProgressValue = progress.OverallPercent ?? progress.StagePercent ?? ProgressValue;
        ProgressText = progress.CurrentItem ?? progress.Stage.ToString();
    }
}
