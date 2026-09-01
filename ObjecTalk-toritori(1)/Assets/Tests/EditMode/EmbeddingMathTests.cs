using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class EmbeddingMathTests
    {
        [Test]
        public void NormalizeInPlace_RejectsZeroVectors()
        {
            var embedding = new[] { 0f, 0f };

            Assert.Throws<System.ArgumentException>(() => EmbeddingMath.NormalizeInPlace(embedding));
        }

        [Test]
        public void NormalizeInPlace_NormalizesFiniteVectorToUnitLength()
        {
            var embedding = new[] { 3f, 4f };

            EmbeddingMath.NormalizeInPlace(embedding);

            Assert.That(embedding[0], Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(embedding[1], Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That((embedding[0] * embedding[0]) + (embedding[1] * embedding[1]), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void RecognitionSample_NormalizesPrivateCopyWithoutMutatingCallerArray()
        {
            var source = new[] { 3f, 4f };

            var sample = new RecognitionSample("apple", source);

            Assert.That(sample.Embedding, Is.Not.SameAs(source));
            Assert.That(source[0], Is.EqualTo(3f));
            Assert.That(source[1], Is.EqualTo(4f));
            Assert.That(sample.Embedding[0], Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(sample.Embedding[1], Is.EqualTo(0.8f).Within(1e-6f));
        }

        [Test]
        public void Cosine_ReturnsNoScoreForUnequalLengths()
        {
            var score = EmbeddingMath.Cosine(new[] { 1f, 0f }, new[] { 1f });

            Assert.That(score.HasValue, Is.False);
        }

        [Test]
        public void FindBest_AggregatesThreeViewsByMaximumScore()
        {
            var samples = Samples(
                ("apple", new[] { 1f, 0f }),
                ("apple", new[] { 0f, 1f }),
                ("apple", new[] { 0.70710677f, 0.70710677f }),
                ("banana", new[] { -1f, 0f }),
                ("banana", new[] { -0.70710677f, 0.70710677f }),
                ("banana", new[] { 0f, -1f }));

            var best = EmbeddingMath.FindBest(samples, new[] { 0f, 1f }, 0.8f, 0.05f);

            Assert.That(best.HasValue, Is.True);
            Assert.That(best.Value.Label, Is.EqualTo("apple"));
            Assert.That(best.Value.Score, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void FindBest_RejectsAmbiguousObjects()
        {
            var samples = Samples(
                ("a", new[] { 1f, 0f }),
                ("b", new[] { 0.99f, 0.01f }));

            Assert.That(EmbeddingMath.FindBest(samples, new[] { 1f, 0f }, 0.8f, 0.02f).HasValue, Is.False);
        }

        [Test]
        public void FindBest_RejectsNonUnitQuery()
        {
            var samples = Samples(("apple", new[] { 1f, 0f }));

            Assert.Throws<System.ArgumentException>(() => EmbeddingMath.FindBest(samples, new[] { 2f, 0f }, 0.8f, 0.02f));
        }

        private static RecognitionSample[] Samples(params (string label, float[] embedding)[] items)
        {
            var samples = new RecognitionSample[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                samples[i] = new RecognitionSample(items[i].label, items[i].embedding);
            }

            return samples;
        }
    }
}
