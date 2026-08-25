namespace DoubleClickCloseTab.Core;

public readonly record struct MouseClick(
    ScreenPoint DownPoint,
    ScreenPoint UpPoint,
    uint DownTimestamp,
    uint UpTimestamp,
    long DownMonotonicTimestampMilliseconds,
    long UpMonotonicTimestampMilliseconds,
    bool IsInjected,
    bool HasModifiers,
    long DownRootWindow,
    long UpRootWindow,
    long DownInputSequence,
    long InputSequence,
    long PointerRevision,
    int MaximumTravelX,
    int MaximumTravelY)
{
    public bool IsEligible(DoubleClickConfiguration configuration) =>
        !IsInjected &&
        !HasModifiers &&
        UpMonotonicTimestampMilliseconds >= DownMonotonicTimestampMilliseconds &&
        DownRootWindow != 0 &&
        DownRootWindow == UpRootWindow &&
        configuration.Contains(DownPoint, UpPoint) &&
        (long)Math.Max(0, MaximumTravelX) * 2 <=
            Math.Max(1, configuration.RectangleWidth) &&
        (long)Math.Max(0, MaximumTravelY) * 2 <=
            Math.Max(1, configuration.RectangleHeight);
}
