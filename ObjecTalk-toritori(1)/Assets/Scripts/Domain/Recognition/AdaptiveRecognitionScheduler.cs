using System;

namespace ObjecTalk.Domain.Recognition
{
    public sealed class AdaptiveRecognitionScheduler
    {
        private readonly double _minimumIntervalSeconds;
        private readonly double _maximumIntervalSeconds;

        private double _nextAllowedTimeSeconds;

        public AdaptiveRecognitionScheduler(RecognitionSettings settings)
        {
            _minimumIntervalSeconds = settings.MinimumRecognitionIntervalSeconds;
            _maximumIntervalSeconds = settings.MaximumRecognitionIntervalSeconds;
            CurrentIntervalSeconds = _minimumIntervalSeconds;
        }

        public int Generation { get; private set; }

        public bool IsBusy { get; private set; }

        public double CurrentIntervalSeconds { get; private set; }

        public double NextAllowedTimeSeconds => _nextAllowedTimeSeconds;

        public bool TryBegin(double nowSeconds)
        {
            ThrowIfNonFinite(nowSeconds, nameof(nowSeconds));

            if (IsBusy || nowSeconds < _nextAllowedTimeSeconds)
            {
                return false;
            }

            IsBusy = true;
            Generation++;
            return true;
        }

        public void Complete(double nowSeconds, double elapsedSeconds)
        {
            ThrowIfNonFinite(nowSeconds, nameof(nowSeconds));

            if (!IsBusy)
            {
                return;
            }

            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and non-negative.");
            }

            IsBusy = false;
            CurrentIntervalSeconds = Math.Clamp(elapsedSeconds, _minimumIntervalSeconds, _maximumIntervalSeconds);
            _nextAllowedTimeSeconds = nowSeconds + CurrentIntervalSeconds;
        }

        public void Cancel()
        {
            IsBusy = false;
            Generation++;
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
