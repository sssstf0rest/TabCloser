namespace DoubleClickCloseTab.Core;

public sealed record TabTarget(
    string Identity,
    long RootWindow,
    ScreenRectangle Bounds);
