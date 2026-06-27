using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Models.Operations;
using FireflyMC.Launcher.Services.Operations;
using FireflyMC.Launcher.Services.SelfUpdate;

namespace FireflyMC.Launcher.ViewModels;

public sealed partial class AboutViewModel(
    ISelfUpdateService selfUpdateService,
    ILauncherOperationCoordinator coordinator) : PageViewModelBase("关于")
{
    [ObservableProperty]
    private string _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private async Task CheckUpdateAsync(CancellationToken cancellationToken)
    {
        using var operation = await coordinator.BeginAsync(LauncherOperationState.SelfUpdating, canCancel: true, cancellationToken);
        var update = await selfUpdateService.CheckAsync(coordinator.CurrentCancellationToken);
        if (update is null)
        {
            Status = "已是最新";
            return;
        }

        Status = $"发现新版本 {update.Tag}";
        await selfUpdateService.StartUpdateAsync(update, coordinator.CurrentCancellationToken);
    }
}
