using System.Collections.ObjectModel;
using System.Windows;

namespace FireflyMC.Launcher.Services.Logging;

public sealed class GameLogService : IGameLogService
{
    public ObservableCollection<string> Lines { get; } = [];

    public void Append(string line)
    {
        void AddLine()
        {
            Lines.Add($"{DateTimeOffset.Now:HH:mm:ss} {line}");
            while (Lines.Count > 2000)
            {
                Lines.RemoveAt(0);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddLine();
        }
        else
        {
            dispatcher.Invoke(AddLine);
        }
    }

    public void Clear()
    {
        Lines.Clear();
    }
}
