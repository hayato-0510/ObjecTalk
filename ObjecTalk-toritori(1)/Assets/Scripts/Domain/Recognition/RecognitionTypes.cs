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

        public RecognitionSettings(float similarityThreshold, float ambiguityMargin)
        {
            if (float.IsNaN(similarityThreshold) || float.IsInfinity(similarityThreshold))
            {
                throw new ArgumentOutOfRangeException(nameof(similarityThreshold), "Similarity threshold must be finite.");
            }

            if (float.IsNaN(ambiguityMargin) || float.IsInfinity(ambiguityMargin) || ambiguityMargin < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ambiguityMargin), "Ambiguity margin must be finite and non-negative.");
            }

            SimilarityThreshold = similarityThreshold;
            AmbiguityMargin = ambiguityMargin;
        }

        public float SimilarityThreshold { get; }

        public float AmbiguityMargin { get; }
    }
}
