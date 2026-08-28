using TabCloser.Core;

namespace TabCloser.Windows.Browser;

internal static class BrowserGesturePolicy
{
    internal const int EdgeNativeCloseSettlingMilliseconds = 200;

    internal static bool IsBrowserEnabled(
        BrowserKind browser,
        bool edgeEnabled) =>
        browser != BrowserKind.Edge || edgeEnabled;

    internal static bool IsTargetEnabled(TabTarget target, bool edgeEnabled) =>
        !BrowserTabPolicy.IsEdgeTarget(target) || edgeEnabled;

    internal static bool RequiresNativeCloseSettling(TabTarget target) =>
        BrowserTabPolicy.IsEdgeTarget(target);

    internal static DoubleClickConfiguration GetInjectionConfiguration(
        TabTarget target,
        DoubleClickConfiguration configuration)
    {
        if (!RequiresNativeCloseSettling(target))
        {
            return configuration;
        }

        return configuration with
        {
            MaximumDelayMilliseconds = checked(
                configuration.MaximumDelayMilliseconds +
                EdgeNativeCloseSettlingMilliseconds),
        };
    }

    internal static bool IsPostReleaseAgeAllowed(
        long ageMilliseconds,
        DoubleClickConfiguration configuration) =>
        ageMilliseconds >= 0 &&
        ageMilliseconds <= configuration.MaximumDelayMilliseconds;
}
