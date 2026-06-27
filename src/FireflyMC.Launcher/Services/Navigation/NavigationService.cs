namespace FireflyMC.Launcher.Services.Navigation;

public sealed class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    private object? _currentPage;

    public object CurrentPage => _currentPage ?? throw new InvalidOperationException("No page has been selected.");
    public event EventHandler? CurrentPageChanged;

    public void NavigateTo<TViewModel>() where TViewModel : notnull
    {
        _currentPage = serviceProvider.GetService(typeof(TViewModel))
            ?? throw new InvalidOperationException($"ViewModel not registered: {typeof(TViewModel).Name}");
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        if (_currentPage is FireflyMC.Launcher.ViewModels.PageViewModelBase page)
        {
            _ = page.InitializeAsync(CancellationToken.None);
        }
    }
}
