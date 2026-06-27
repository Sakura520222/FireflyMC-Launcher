using System.Diagnostics;
using System.Windows;
using FireflyMC.Launcher.Services.Accounts.Microsoft;

namespace FireflyMC.Launcher.Views;

public partial class DeviceCodeDialog : Window
{
    private readonly IDeviceCodeLoginSession _session;

    public DeviceCodeDialog(IDeviceCodeLoginSession session)
    {
        _session = session;
        InitializeComponent();
        DataContext = new
        {
            VerificationUri = session.DeviceCode.VerificationUri,
            UserCode = session.DeviceCode.UserCode,
            ExpiresText = $"剩余 {Math.Max(0, (int)(session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)} 秒"
        };
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_session.DeviceCode.UserCode);
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_session.DeviceCode.VerificationUri) { UseShellExecute = true });
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _session.Cancel();
        DialogResult = false;
    }
}
