namespace TabCloser.Core;

public sealed class DoubleClickDetector
{
    private Candidate? _firstClick;

    public bool Register(
        MouseClick click,
        TabTarget? target,
        DoubleClickConfiguration configuration)
    {
        if (!click.IsEligible(configuration) ||
            target is null ||
            target.RootWindow != click.DownRootWindow)
        {
            Reset();
            return false;
        }

        Candidate current = new(click, target);
        Candidate? first = _firstClick;

        if (first is null)
        {
            _firstClick = current;
            return false;
        }

        uint elapsed = unchecked(click.DownTimestamp - first.Click.DownTimestamp);
        long monotonicElapsed = click.DownMonotonicTimestampMilliseconds -
            first.Click.DownMonotonicTimestampMilliseconds;
        bool matches =
            elapsed <= configuration.MaximumDelayMilliseconds &&
            monotonicElapsed >= 0 &&
            monotonicElapsed <= configuration.MaximumDelayMilliseconds &&
            configuration.Contains(first.Click.DownPoint, click.DownPoint) &&
            configuration.Contains(first.Click.UpPoint, click.UpPoint) &&
            first.Target.RootWindow == target.RootWindow &&
            string.Equals(
                first.Target.Identity,
                target.Identity,
                StringComparison.Ordinal);

        _firstClick = matches ? null : current;
        return matches;
    }

    public void Reset() => _firstClick = null;

    private sealed record Candidate(MouseClick Click, TabTarget Target);
}
