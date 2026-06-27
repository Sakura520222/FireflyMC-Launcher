using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Infrastructure.Storage;

namespace FireflyMC.Launcher.ViewModels;

public sealed partial class SettingsViewModel(ISettingsStore settingsStore) : PageViewModelBase("设置")
{
    [ObservableProperty]
    private bool _automaticMemory = true;

    [ObservableProperty]
    private int _minMemoryMb = 1024;

    [ObservableProperty]
    private int _maxMemoryMb = 4096;

    [ObservableProperty]
    private string _additionalJvmArgs = "";

    [ObservableProperty]
    private string? _javaPathOverride;

    [ObservableProperty]
    private bool _useMirror = true;

    [ObservableProperty]
    private bool _autoJoinServer = true;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private bool _recordNetworkDiagnostics;

    [ObservableProperty]
    private bool _showUsernameAndUuidInLogs = true;

    [ObservableProperty]
    private string _status = "";

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        AutomaticMemory = settings.AutomaticMemory;
        MinMemoryMb = settings.MinMemoryMb;
        MaxMemoryMb = settings.MaxMemoryMb;
        AdditionalJvmArgs = settings.AdditionalJvmArgs;
        JavaPathOverride = settings.JavaPathOverride;
        UseMirror = settings.UseMirror;
        AutoJoinServer = settings.AutoJoinServer;
        AutoCheckUpdates = settings.AutoCheckUpdates;
        RecordNetworkDiagnostics = settings.RecordNetworkDiagnostics;
        ShowUsernameAndUuidInLogs = settings.ShowUsernameAndUuidInLogs;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var current = await settingsStore.LoadAsync(cancellationToken);
        var settings = current with
        {
            AutomaticMemory = AutomaticMemory,
            MinMemoryMb = MinMemoryMb,
            MaxMemoryMb = MaxMemoryMb,
            AdditionalJvmArgs = AdditionalJvmArgs,
            JavaPathOverride = string.IsNullOrWhiteSpace(JavaPathOverride) ? null : JavaPathOverride,
            UseMirror = UseMirror,
            AutoJoinServer = AutoJoinServer,
            AutoCheckUpdates = AutoCheckUpdates,
            RecordNetworkDiagnostics = RecordNetworkDiagnostics,
            ShowUsernameAndUuidInLogs = ShowUsernameAndUuidInLogs
        };
        await settingsStore.SaveAsync(settings, cancellationToken);
        Status = "已保存";
    }
}
