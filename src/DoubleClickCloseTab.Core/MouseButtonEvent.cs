namespace DoubleClickCloseTab.Core;

public enum MouseButtonEventKind
{
    LeftDown,
    LeftUp,
    OtherButton,
}

public readonly record struct MouseButtonEvent(
    MouseButtonEventKind Kind,
    ScreenPoint Point,
    uint Timestamp,
    long MonotonicTimestampMilliseconds,
    bool IsInjected,
    bool HasModifiers,
    long RootWindow,
    long InputSequence,
    long PointerRevision,
    int MaximumTravelX = 0,
    int MaximumTravelY = 0);
