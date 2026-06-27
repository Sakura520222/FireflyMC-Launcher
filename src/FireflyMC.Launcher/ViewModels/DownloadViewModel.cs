using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Models;
using FireflyMC.Launcher.Models.Operations;
using FireflyMC.Launcher.Services.Install;
using FireflyMC.Launcher.Services.Operations;
using FireflyMC.Launcher.Services.Update;

namespace FireflyMC.Launcher.ViewModels;

public sealed record ModListItem(string Name, string FileName, string Status);

public sealed partial class DownloadViewModel(
    IModPackUpdateService updateService,
    IInstallService installService,
    ILauncherOperationCoordinator coordinator) : PageViewModelBase("整合包管理")
{
    [ObservableProperty]
    private string _status = "未检查";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _versionInfo = "";

    public ObservableCollection<ModListItem> Mods { get; } = [];

    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var operation = await coordinator.BeginAsync(LauncherOperationState.Checking, canCancel: true, cancellationToken);
            var remote = await updateService.ResolveRemoteManifestAsync(coordinator.CurrentCancellationToken);
            var plan = await updateService.BuildUpdatePlanAsync(remote, forceVerify: false, coordinator.CurrentCancellationToken);
            Mods.Clear();
            foreach (var mod in remote.Mods)
            {
                var relative = $"mods/{mod.FileName}";
                var status = plan.Downloads.Any(d => d.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase))
                    ? "等待下载"
                    : "已是最新";
                Mods.Add(new ModListItem(mod.Name, mod.FileName, status));
            }

            VersionInfo = $"整合包 {remote.PackVersion} / Manifest {remote.ManifestId}";
            Status = plan.Downloads.Count == 0 && plan.Deletes.Count == 0 ? "已是最新" : $"需要下载 {plan.Downloads.Count} 个文件，删除 {plan.Deletes.Count} 个旧文件";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
    }

    [RelayCommand]
    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var operation = await coordinator.BeginAsync(LauncherOperationState.Updating, canCancel: true, cancellationToken);
            var progress = new Progress<StageProgress>(p =>
            {
                ProgressValue = p.OverallPercent ?? p.StagePercent ?? ProgressValue;
                Status = p.CurrentItem ?? p.Stage.ToString();
            });
            var remote = await updateService.SyncAsync(progress, coordinator.CurrentCancellationToken);
            VersionInfo = $"整合包 {remote.PackVersion} / Manifest {remote.ManifestId}";
            Status = "更新完成";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
    }

    [RelayCommand]
    private async Task RepairAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var operation = await coordinator.BeginAsync(LauncherOperationState.Repairing, canCancel: true, cancellationToken);
            var progress = new Progress<StageProgress>(p =>
            {
                ProgressValue = p.OverallPercent ?? p.StagePercent ?? ProgressValue;
                Status = p.CurrentItem ?? p.Stage.ToString();
            });
            await installService.RepairAsync(progress, coordinator.CurrentCancellationToken);
            Status = "修复完成";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
    }
}
