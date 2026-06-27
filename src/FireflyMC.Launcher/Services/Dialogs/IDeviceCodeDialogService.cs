using FireflyMC.Launcher.Services.Accounts.Microsoft;

namespace FireflyMC.Launcher.Services.Dialogs;

public interface IDeviceCodeDialogService
{
    bool Show(IDeviceCodeLoginSession session);
}
