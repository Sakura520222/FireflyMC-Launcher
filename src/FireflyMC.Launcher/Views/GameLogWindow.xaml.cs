using System.Windows;
using FireflyMC.Launcher.Services.Logging;

namespace FireflyMC.Launcher.Views;

public partial class GameLogWindow : Window
{
    public GameLogWindow(IGameLogService logService)
    {
        InitializeComponent();
        DataContext = logService;
    }
}
