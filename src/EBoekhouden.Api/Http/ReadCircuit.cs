namespace EBoekhouden.Api.Http;

// Shared across calls. Generations prevent late completions from changing a newer circuit state.
internal sealed class ReadCircuit(TimeProvider time, int failureThreshold, TimeSpan breakDuration)
{
    private readonly object _sync = new();
    private int _failures;
    private long _generation;
    private DateTimeOffset? _openUntil;
    private bool _probeActive;

    public Permit Enter()
    {
        lock (_sync)
        {
            if (_openUntil is { } until)
            {
                if (time.GetUtcNow() < until || _probeActive)
                    throw new ApiTransportException(TransportFailure.CircuitOpen);
                _probeActive = true;
                return new Permit(this, _generation, true);
            }
            return new Permit(this, _generation, false);
        }
    }

    private void Complete(long generation, bool probe, bool? success)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            if (probe) _probeActive = false;
            if (success is null) return;
            if (success.Value)
            {
                _failures = 0;
                if (probe) { _openUntil = null; _generation++; }
            }
            else if (probe || ++_failures >= failureThreshold)
            {
                _openUntil = time.GetUtcNow() + breakDuration;
                _generation++;
            }
        }
    }

    internal sealed class Permit(ReadCircuit owner, long generation, bool probe) : IDisposable
    {
        private bool _completed;
        public void Success() => Complete(true);
        public void Failure() => Complete(false);
        public void Dispose() => Complete(null);
        private void Complete(bool? success)
        {
            if (_completed) return;
            _completed = true;
            owner.Complete(generation, probe, success);
        }
    }
}
