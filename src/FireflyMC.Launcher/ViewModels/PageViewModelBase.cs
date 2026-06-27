using CommunityToolkit.Mvvm.ComponentModel;

namespace FireflyMC.Launcher.ViewModels;

public abstract class PageViewModelBase(string title) : ObservableObject
{
    public string Title { get; } = title;

    public virtual Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
