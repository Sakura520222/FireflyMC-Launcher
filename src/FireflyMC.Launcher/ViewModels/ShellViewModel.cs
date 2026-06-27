using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Models.Operations;
using FireflyMC.Launcher.Services.Navigation;
using FireflyMC.Launcher.Services.Operations;

namespace FireflyMC.Launcher.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ILauncherOperationCoordinator _coordinator;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _operationText = "就绪";

    [ObservableProperty]
    private bool _canCancel;

    public ShellViewModel(INavigationService navigationService, ILauncherOperationCoordinator coordinator)
    {
        _navigationService = navigationService;
        _coordinator = coordinator;
        NavigationItems =
        [
            new("主页", typeof(HomeViewModel)),
            new("账号", typeof(AccountViewModel)),
            new("整合包管理", typeof(DownloadViewModel)),
            new("设置", typeof(SettingsViewModel)),
            new("关于", typeof(AboutViewModel))
        ];
        _navigationService.CurrentPageChanged += (_, _) => CurrentPage = _navigationService.CurrentPage;
        _coordinator.StateChanged += (_, _) => UpdateOperationState();
        _navigationService.NavigateTo<HomeViewModel>();
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    [RelayCommand]
    private void Navigate(NavigationItemViewModel item)
    {
        var method = typeof(INavigationService).GetMethod(nameof(INavigationService.NavigateTo))!;
        method.MakeGenericMethod(item.ViewModelType).Invoke(_navigationService, null);
    }

    [RelayCommand]
    private void Cancel()
    {
        _coordinator.Cancel();
    }

    private void UpdateOperationState()
    {
        OperationText = _coordinator.State switch
        {
            LauncherOperationState.Idle => "就绪",
            LauncherOperationState.Checking => "正在检查...",
            LauncherOperationState.Installing => "正在安装",
            LauncherOperationState.Updating => "正在更新",
            LauncherOperationState.Repairing => "正在修复",
            LauncherOperationState.PreparingLaunch => "正在准备启动",
            LauncherOperationState.Launching => "正在启动",
            LauncherOperationState.GameRunning => "游戏运行中",
            LauncherOperationState.SelfUpdating => "正在自更新",
            LauncherOperationState.Recovering => "正在恢复事务",
            LauncherOperationState.Failed => "操作失败",
            _ => _coordinator.State.ToString()
        };
        CanCancel = _coordinator.CanCancel;
    }
}
