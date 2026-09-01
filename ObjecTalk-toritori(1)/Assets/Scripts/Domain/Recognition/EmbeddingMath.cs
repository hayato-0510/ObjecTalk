using System;
using System.Collections.Generic;

namespace ObjecTalk.Domain.Recognition
{
    public static class EmbeddingMath
    {
        private const float ZeroTolerance = 1e-12f;

        public static void NormalizeInPlace(float[] embedding)
        {
            var norm = ThrowIfInvalid(embedding, nameof(embedding));
            var scale = 1f / norm;

            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] *= scale;
            }
        }

        public static float? Cosine(float[] left, float[] right)
        {
            if (!TryGetNorm(left, out var leftNorm) || !TryGetNorm(right, out var rightNorm))
            {
                return null;
            }

            if (left.Length != right.Length)
            {
                return null;
            }

            var dot = 0f;
            for (var i = 0; i < left.Length; i++)
            {
                dot += left[i] * right[i];
            }

            return dot / (leftNorm * rightNorm);
        }

        public static RecognitionCandidate? FindBest(
            IReadOnlyList<RecognitionSample> samples,
            float[] query,
            float threshold,
            float ambiguityMargin)
        {
            if (samples == null || samples.Count == 0)
            {
                return null;
            }

            if (float.IsNaN(threshold) || float.IsInfinity(threshold))
            {
                throw new ArgumentOutOfRangeException(nameof(threshold), "Recognition threshold must be finite.");
            }

            if (float.IsNaN(ambiguityMargin) || float.IsInfinity(ambiguityMargin) || ambiguityMargin < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ambiguityMargin), "Ambiguity margin must be finite and non-negative.");
            }

            if (!TryGetNorm(query, out _))
            {
                return null;
            }

            var bestScoresByLabel = new Dictionary<string, float>(StringComparer.Ordinal);

            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var score = Cosine(sample.Embedding, query);
                if (!score.HasValue)
                {
                    continue;
                }

                if (bestScoresByLabel.TryGetValue(sample.Label, out var currentBest))
                {
                    if (score.Value > currentBest)
                    {
                        bestScoresByLabel[sample.Label] = score.Value;
                    }

                    continue;
                }

                bestScoresByLabel.Add(sample.Label, score.Value);
            }

            if (bestScoresByLabel.Count == 0)
            {
                return null;
            }

            var bestLabel = default(string);
            var bestScore = float.NegativeInfinity;
            var secondBestScore = float.NegativeInfinity;

            foreach (var pair in bestScoresByLabel)
            {
                if (pair.Value > bestScore)
                {
                    secondBestScore = bestScore;
                    bestScore = pair.Value;
                    bestLabel = pair.Key;
                }
                else if (pair.Value > secondBestScore)
                {
                    secondBestScore = pair.Value;
                }
            }

            if (bestScore < threshold)
            {
                return null;
            }

            if (!float.IsNegativeInfinity(secondBestScore) && (bestScore - secondBestScore) < ambiguityMargin)
            {
                return null;
            }

            return new RecognitionCandidate(bestLabel, bestScore);
        }

        internal static float ThrowIfInvalid(float[] embedding, string paramName)
        {
            if (!TryGetNorm(embedding, out var norm))
            {
                throw new ArgumentException("Embeddings must be non-empty, finite, and non-zero.", paramName);
            }

            return norm;
        }

        private static bool TryGetNorm(float[] embedding, out float norm)
        {
            norm = 0f;

            if (embedding == null || embedding.Length == 0)
            {
                return false;
            }

            var normSquared = 0f;
            for (var i = 0; i < embedding.Length; i++)
            {
                var value = embedding[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }

                normSquared += value * value;
            }

            if (normSquared <= ZeroTolerance)
            {
                return false;
            }

            norm = (float)Math.Sqrt(normSquared);
            return true;
        }
    }
}
