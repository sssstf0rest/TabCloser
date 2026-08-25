namespace DoubleClickCloseTab.Core;

public sealed class MouseClickAssembler
{
    private MouseButtonEvent? _leftDown;

    public ClickAssemblyResult Register(MouseButtonEvent mouseEvent)
    {
        switch (mouseEvent.Kind)
        {
            case MouseButtonEventKind.LeftDown:
                {
                    bool resetSequence = _leftDown is not null;
                    _leftDown = mouseEvent;
                    return new ClickAssemblyResult(null, resetSequence);
                }

            case MouseButtonEventKind.LeftUp:
                {
                    MouseButtonEvent? down = _leftDown;
                    _leftDown = null;
                    if (down is null)
                    {
                        return new ClickAssemblyResult(null, ResetSequence: true);
                    }

                    MouseClick click = new(
                        down.Value.Point,
                        mouseEvent.Point,
                        down.Value.Timestamp,
                        mouseEvent.Timestamp,
                        down.Value.MonotonicTimestampMilliseconds,
                        mouseEvent.MonotonicTimestampMilliseconds,
                        down.Value.IsInjected || mouseEvent.IsInjected,
                        down.Value.HasModifiers || mouseEvent.HasModifiers,
                        down.Value.RootWindow,
                        mouseEvent.RootWindow,
                        down.Value.InputSequence,
                        mouseEvent.InputSequence,
                        mouseEvent.PointerRevision,
                        mouseEvent.MaximumTravelX,
                        mouseEvent.MaximumTravelY);
                    return new ClickAssemblyResult(click, ResetSequence: false);
                }

            case MouseButtonEventKind.OtherButton:
                _leftDown = null;
                return new ClickAssemblyResult(null, ResetSequence: true);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mouseEvent),
                    mouseEvent.Kind,
                    "Unknown mouse event kind.");
        }
    }

    public void Reset() => _leftDown = null;
}

public readonly record struct ClickAssemblyResult(
    MouseClick? Click,
    bool ResetSequence);
