using System.Collections.ObjectModel;

namespace FireflyMC.Launcher.Services.Logging;

public interface IGameLogService
{
    ObservableCollection<string> Lines { get; }
    void Append(string line);
    void Clear();
}
