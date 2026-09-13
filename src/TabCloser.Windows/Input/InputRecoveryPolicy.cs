namespace TabCloser.Windows.Input;

internal readonly record struct InputSendOutcome(uint Requested, uint Inserted, int? Win32Error);

// One bounded recovery attempt per failure episode. Time passing or toggling
// Enabled cannot rearm it; only a subsequent complete send can do that.
internal sealed class InputRecoveryPolicy
{
    internal const long EvidenceWindowMilliseconds = 10_000;
    internal const long CooldownMilliseconds = 60_000;
    private readonly object _gate = new();
    private int _denials;
    private long _lastDenial;
    private long? _lastAttempt;
    private bool _armed = true;

    internal void Observe(InputSendOutcome outcome, long now)
    {
        lock (_gate)
        {
            if (outcome.Requested == 2 && outcome.Inserted == 2)
            {
                _denials = 0;
                _armed = true;
                return;
            }

            if (outcome.Requested != 2 || outcome.Inserted != 0 || outcome.Win32Error != 5 ||
                !_armed || IsCoolingDown(now))
            {
                _denials = 0;
                return;
            }

            if (now < _lastDenial || now - _lastDenial > EvidenceWindowMilliseconds)
            {
                _denials = 0;
            }

            _lastDenial = now;
            _denials = Math.Min(2, _denials + 1);
        }
    }

    internal bool TryTakeRequest(long now)
    {
        lock (_gate)
        {
            if (_denials < 2 || !_armed || IsCoolingDown(now))
            {
                return false;
            }

            _denials = 0;
            if (now < _lastDenial || now - _lastDenial > EvidenceWindowMilliseconds)
            {
                return false;
            }

            _armed = false;
            _lastAttempt = now;
            return true;
        }
    }

    internal void ClearEvidence()
    {
        lock (_gate)
        {
            _denials = 0;
        }
    }

    private bool IsCoolingDown(long now) =>
        _lastAttempt is long previous && (now < previous || now - previous < CooldownMilliseconds);
}
