using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FireflyMC.Launcher.Models.Accounts;
using FireflyMC.Launcher.Services.Accounts;
using FireflyMC.Launcher.Services.Dialogs;

namespace FireflyMC.Launcher.ViewModels;

public sealed partial class AccountViewModel(
    IAccountService accountService,
    IDeviceCodeDialogService deviceCodeDialogService) : PageViewModelBase("账号")
{
    [ObservableProperty]
    private AccountProfile? _selectedAccount;

    [ObservableProperty]
    private string _offlineUsername = "";

    [ObservableProperty]
    private string _status = "";

    public ObservableCollection<AccountProfile> Accounts { get; } = [];

    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Accounts.Clear();
        foreach (var account in await accountService.GetAccountsAsync(cancellationToken))
        {
            Accounts.Add(account);
        }

        SelectedAccount ??= Accounts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task AddOfflineAsync(CancellationToken cancellationToken)
    {
        var profile = await accountService.AddOfflineAsync(OfflineUsername, cancellationToken);
        OfflineUsername = "";
        Status = $"已添加离线账号 {profile.Username}";
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task AddMicrosoftAsync(CancellationToken cancellationToken)
    {
        using var session = await accountService.StartMicrosoftLoginAsync(cancellationToken);
        if (!deviceCodeDialogService.Show(session))
        {
            session.Cancel();
            return;
        }

        var profile = await accountService.CompleteMicrosoftLoginAsync(session, cancellationToken);
        Status = $"已登录 {profile.Username}";
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (SelectedAccount is null)
        {
            return;
        }

        await accountService.LogoutAsync(SelectedAccount.Id, cancellationToken);
        SelectedAccount = null;
        await RefreshAsync(cancellationToken);
    }
}
