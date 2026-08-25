namespace TabCloser.Core;

public readonly record struct ScreenRectangle(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public bool Contains(ScreenPoint point) =>
        point.X >= Left &&
        point.X < Right &&
        point.Y >= Top &&
        point.Y < Bottom;
}
