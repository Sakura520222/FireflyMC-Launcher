using System.Windows;
using FireflyMC.Launcher.ViewModels;

namespace FireflyMC.Launcher;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
