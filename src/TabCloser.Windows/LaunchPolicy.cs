namespace TabCloser.Windows;

internal enum LaunchAction
{
    RunVisible,
    RunUsingSavedVisibility,
    RequestTrayIconRestore,
    Exit,
}

internal static class LaunchPolicy
{
    public static LaunchAction Decide(bool isPrimary, bool startedWithWindows)
    {
        if (isPrimary)
        {
            return startedWithWindows
                ? LaunchAction.RunUsingSavedVisibility
                : LaunchAction.RunVisible;
        }

        return startedWithWindows
            ? LaunchAction.Exit
            : LaunchAction.RequestTrayIconRestore;
    }
}
