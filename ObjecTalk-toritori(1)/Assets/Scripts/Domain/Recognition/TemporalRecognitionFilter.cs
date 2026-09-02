using System;

namespace ObjecTalk.Domain.Recognition
{
    public sealed class TemporalRecognitionFilter
    {
        private readonly int _requiredConsecutiveHits;
        private readonly double _lossGracePeriodSeconds;

        private RecognitionCandidate? _current;
        private RecognitionCandidate? _pending;
        private int _pendingHitCount;
        private double _lastStableObservationSeconds;

        public TemporalRecognitionFilter(RecognitionSettings settings)
        {
            _requiredConsecutiveHits = settings.RequiredConsecutiveHits;
            _lossGracePeriodSeconds = settings.LossGracePeriodSeconds;
        }

        public RecognitionCandidate? Current => _current;

        public RecognitionCandidate? Observe(RecognitionCandidate? candidate, double nowSeconds)
        {
            ThrowIfNonFinite(nowSeconds, nameof(nowSeconds));

            if (_current.HasValue && (nowSeconds - _lastStableObservationSeconds) > _lossGracePeriodSeconds)
            {
                _current = null;
            }

            if (!candidate.HasValue)
            {
                ClearPending();
                return _current;
            }

            if (_current.HasValue && StringComparer.Ordinal.Equals(_current.Value.Label, candidate.Value.Label))
            {
                _current = candidate;
                _lastStableObservationSeconds = nowSeconds;
                ClearPending();
                return _current;
            }

            AdvancePending(candidate.Value);

            if (_pendingHitCount >= _requiredConsecutiveHits)
            {
                _current = _pending;
                _lastStableObservationSeconds = nowSeconds;
                ClearPending();
            }

            return _current;
        }

        private void AdvancePending(RecognitionCandidate candidate)
        {
            if (_pending.HasValue && StringComparer.Ordinal.Equals(_pending.Value.Label, candidate.Label))
            {
                _pending = candidate;
                _pendingHitCount++;
                return;
            }

            _pending = candidate;
            _pendingHitCount = 1;
        }

        private void ClearPending()
        {
            _pending = null;
            _pendingHitCount = 0;
        }

        private static void ThrowIfNonFinite(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, "Time values must be finite.");
            }
        }
    }
}
