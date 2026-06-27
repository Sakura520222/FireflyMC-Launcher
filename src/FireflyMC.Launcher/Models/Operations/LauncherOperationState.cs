namespace FireflyMC.Launcher.Models.Operations;

public enum LauncherOperationState
{
    Idle,
    Checking,
    Installing,
    Updating,
    Repairing,
    PreparingLaunch,
    Launching,
    GameRunning,
    SelfUpdating,
    Recovering,
    Failed
}
