namespace TabCloser.Core;

public readonly record struct DoubleClickConfiguration(
    uint MaximumDelayMilliseconds,
    int RectangleWidth,
    int RectangleHeight)
{
    public bool Contains(ScreenPoint first, ScreenPoint second)
    {
        long deltaX = Math.Abs((long)second.X - first.X);
        long deltaY = Math.Abs((long)second.Y - first.Y);

        return deltaX * 2 <= Math.Max(1, RectangleWidth) &&
               deltaY * 2 <= Math.Max(1, RectangleHeight);
    }
}
