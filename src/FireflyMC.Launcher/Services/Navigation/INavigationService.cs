namespace FireflyMC.Launcher.Services.Navigation;

public interface INavigationService
{
    object CurrentPage { get; }
    event EventHandler? CurrentPageChanged;
    void NavigateTo<TViewModel>() where TViewModel : notnull;
}
