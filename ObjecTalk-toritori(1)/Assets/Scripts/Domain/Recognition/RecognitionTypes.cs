using System;

namespace ObjecTalk.Domain.Recognition
{
    [Serializable]
    public readonly struct RecognitionSample
    {
        public RecognitionSample(string label, float[] embedding)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Recognition samples require a label.", nameof(label));
            }

            Label = label;
            Embedding = EmbeddingMath.CopyNormalized(embedding, nameof(embedding));
        }

        public string Label { get; }

        public float[] Embedding { get; }
    }

    [Serializable]
    public readonly struct RecognitionCandidate
    {
        public RecognitionCandidate(string label, float score)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Recognition candidates require a label.", nameof(label));
            }

            if (float.IsNaN(score) || float.IsInfinity(score))
            {
                throw new ArgumentOutOfRangeException(nameof(score), "Recognition scores must be finite.");
            }

            Label = label;
            Score = score;
        }

        public string Label { get; }

        public float Score { get; }
    }

    [Serializable]
    public readonly struct RecognitionSettings
    {
        public const int RequiredSamplesPerObject = 3;
        public const int DefaultRequiredConsecutiveHits = 2;
        public const float DefaultLossGracePeriodSeconds = 0.4f;
        public const float MinimumSupportedRecognitionIntervalSeconds = 0.2f;
        public const float MaximumSupportedRecognitionIntervalSeconds = 0.5f;

        public RecognitionSettings(float similarityThreshold, float ambiguityMargin)
            : this(
                similarityThreshold,
                ambiguityMargin,
                DefaultRequiredConsecutiveHits,
                DefaultLossGracePeriodSeconds,
                MinimumSupportedRecognitionIntervalSeconds,
                MaximumSupportedRecognitionIntervalSeconds)
        {
        }

        public RecognitionSettings(
            float similarityThreshold,
            float ambiguityMargin,
            int requiredConsecutiveHits,
            float lossGracePeriodSeconds,
            float minimumRecognitionIntervalSeconds,
            float maximumRecognitionIntervalSeconds)
        {
            if (float.IsNaN(similarityThreshold) || float.IsInfinity(similarityThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(similarityThreshold), "Similarity threshold must be finite.");
            }

            if (float.IsNaN(ambiguityMargin) || float.IsInfinity(ambiguityMargin) || ambiguityMargin < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ambiguityMargin), "Ambiguity margin must be finite and non-negative.");
            }

            if (requiredConsecutiveHits < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveHits), "Required consecutive hits must be at least one.");
            }

            if (float.IsNaN(lossGracePeriodSeconds) || float.IsInfinity(lossGracePeriodSeconds) || lossGracePeriodSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(lossGracePeriodSeconds), "Loss grace period must be finite and non-negative.");
            }

            var clampedMinimumIntervalSeconds = ClampRecognitionInterval(
                minimumRecognitionIntervalSeconds,
                nameof(minimumRecognitionIntervalSeconds));
            var clampedMaximumIntervalSeconds = ClampRecognitionInterval(
                maximumRecognitionIntervalSeconds,
                nameof(maximumRecognitionIntervalSeconds));

            if (clampedMinimumIntervalSeconds > clampedMaximumIntervalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRecognitionIntervalSeconds),
                    "Maximum recognition interval must be greater than or equal to the minimum interval.");
            }

            SimilarityThreshold = similarityThreshold;
            AmbiguityMargin = ambiguityMargin;
            RequiredConsecutiveHits = requiredConsecutiveHits;
            LossGracePeriodSeconds = lossGracePeriodSeconds;
            MinimumRecognitionIntervalSeconds = clampedMinimumIntervalSeconds;
            MaximumRecognitionIntervalSeconds = clampedMaximumIntervalSeconds;
        }

        public float SimilarityThreshold { get; }

        public float AmbiguityMargin { get; }

        public int RequiredConsecutiveHits { get; }

        public float LossGracePeriodSeconds { get; }

        public float MinimumRecognitionIntervalSeconds { get; }

        public float MaximumRecognitionIntervalSeconds { get; }

        private static float ClampRecognitionInterval(float seconds, string paramName)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(paramName, "Recognition intervals must be finite and greater than zero.");
            }

            return Math.Clamp(seconds, MinimumSupportedRecognitionIntervalSeconds, MaximumSupportedRecognitionIntervalSeconds);
        }
    }
}
