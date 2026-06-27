using FireflyMC.Launcher.Services.Accounts.Microsoft;
using FireflyMC.Launcher.Services.Dialogs;

namespace FireflyMC.Launcher.Views;

public sealed class DeviceCodeDialogService : IDeviceCodeDialogService
{
    public bool Show(IDeviceCodeLoginSession session)
    {
        var dialog = new DeviceCodeDialog(session)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true;
    }
}
