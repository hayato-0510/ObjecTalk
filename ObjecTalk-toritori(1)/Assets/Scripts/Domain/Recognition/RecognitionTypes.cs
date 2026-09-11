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
        public const int DefaultMinimumLocalMatchCount = 10;

        /// <summary>
        /// Correlation a template must reach before its position is believed. Measured on
        /// synthetic frames: combined move+zoom+tilt scored 0.38, the weakest true match 0.31.
        /// A starting point for the device pass, not a settled value.
        /// </summary>
        public const float DefaultMinimumTemplateScore = 0.35f;

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
            : this(
                similarityThreshold,
                ambiguityMargin,
                requiredConsecutiveHits,
                lossGracePeriodSeconds,
                minimumRecognitionIntervalSeconds,
                maximumRecognitionIntervalSeconds,
                DefaultMinimumLocalMatchCount)
        {
        }

        public RecognitionSettings(
            float similarityThreshold,
            float ambiguityMargin,
            int requiredConsecutiveHits,
            float lossGracePeriodSeconds,
            float minimumRecognitionIntervalSeconds,
            float maximumRecognitionIntervalSeconds,
            int minimumLocalMatchCount)
            : this(
                similarityThreshold,
                ambiguityMargin,
                requiredConsecutiveHits,
                lossGracePeriodSeconds,
                minimumRecognitionIntervalSeconds,
                maximumRecognitionIntervalSeconds,
                minimumLocalMatchCount,
                DefaultMinimumTemplateScore,
                false)
        {
        }

        public RecognitionSettings(
            float similarityThreshold,
            float ambiguityMargin,
            int requiredConsecutiveHits,
            float lossGracePeriodSeconds,
            float minimumRecognitionIntervalSeconds,
            float maximumRecognitionIntervalSeconds,
            int minimumLocalMatchCount,
            float minimumTemplateScore,
            bool templateGatesRecognition)
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

            if (minimumLocalMatchCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumLocalMatchCount),
                    "Local verification requires at least one matching descriptor.");
            }

            // Correlation is bounded by definition; a threshold outside it can never be met, or
            // can never be missed, and either way the caller has made a mistake worth reporting.
            if (float.IsNaN(minimumTemplateScore)
                || float.IsInfinity(minimumTemplateScore)
                || minimumTemplateScore < -1f
                || minimumTemplateScore > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumTemplateScore),
                    "A correlation threshold must lie within [-1, 1].");
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
            MinimumLocalMatchCount = minimumLocalMatchCount;
            MinimumTemplateScore = minimumTemplateScore;
            TemplateGatesRecognition = templateGatesRecognition;
        }

        public float SimilarityThreshold { get; }

        public float AmbiguityMargin { get; }

        public int RequiredConsecutiveHits { get; }

        public float LossGracePeriodSeconds { get; }

        public float MinimumRecognitionIntervalSeconds { get; }

        public float MaximumRecognitionIntervalSeconds { get; }

        /// <summary>
        /// Descriptor matches AKAZE must find before it confirms the best neural candidate.
        /// Kept here so no production call site hard-codes a verification threshold.
        /// </summary>
        public int MinimumLocalMatchCount { get; }

        /// <summary>Correlation a template must reach before its position is believed.</summary>
        public float MinimumTemplateScore { get; }

        /// <summary>
        /// Whether a template that fails to match may reject a candidate the embedding accepted.
        /// Off until real device scores have been measured; on is a deliberate second step.
        /// </summary>
        public bool TemplateGatesRecognition { get; }

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
