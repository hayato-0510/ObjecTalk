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

        public int? TryBegin(double nowSeconds)
        {
            ThrowIfNonFinite(nowSeconds, nameof(nowSeconds));

            if (IsBusy || nowSeconds < _nextAllowedTimeSeconds)
            {
                return null;
            }

            IsBusy = true;
            Generation++;
            return Generation;
        }

        public bool Complete(int generation, double nowSeconds, double elapsedSeconds)
        {
            ThrowIfNonFinite(nowSeconds, nameof(nowSeconds));

            if (generation < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(generation), "Generation tokens must be positive.");
            }

            if (!IsBusy || generation != Generation)
            {
                return false;
            }

            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and non-negative.");
            }

            IsBusy = false;
            CurrentIntervalSeconds = Math.Clamp(elapsedSeconds, _minimumIntervalSeconds, _maximumIntervalSeconds);
            _nextAllowedTimeSeconds = nowSeconds + CurrentIntervalSeconds;
            return true;
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
