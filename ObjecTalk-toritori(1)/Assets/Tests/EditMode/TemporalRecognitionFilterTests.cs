using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class TemporalRecognitionFilterTests
    {
        [Test]
        public void Observe_PublishesInitialRecognitionOnlyAfterRequiredConsecutiveHits()
        {
            var filter = new TemporalRecognitionFilter(Settings(requiredConsecutiveHits: 3));
            var candidate = new RecognitionCandidate("apple", 0.91f);

            Assert.That(filter.Observe(candidate, 0.0), Is.Null);
            Assert.That(filter.Observe(candidate, 0.1), Is.Null);

            var stabilized = filter.Observe(candidate, 0.2);

            Assert.That(stabilized.HasValue, Is.True);
            Assert.That(stabilized.Value.Label, Is.EqualTo("apple"));
            Assert.That(stabilized.Value.Score, Is.EqualTo(0.91f).Within(1e-6f));
        }

        [Test]
        public void Observe_KeepsCurrentIdentityUntilReplacementIsRepeated()
        {
            var filter = new TemporalRecognitionFilter(Settings(requiredConsecutiveHits: 2, lossGracePeriodSeconds: 1f));

            filter.Observe(new RecognitionCandidate("apple", 0.90f), 0.0);
            var initial = filter.Observe(new RecognitionCandidate("apple", 0.92f), 0.1);
            var singleReplacementObservation = filter.Observe(new RecognitionCandidate("banana", 0.88f), 0.2);
            var switched = filter.Observe(new RecognitionCandidate("banana", 0.89f), 0.3);

            Assert.That(initial.HasValue, Is.True);
            Assert.That(initial.Value.Label, Is.EqualTo("apple"));
            Assert.That(singleReplacementObservation.HasValue, Is.True);
            Assert.That(singleReplacementObservation.Value.Label, Is.EqualTo("apple"));
            Assert.That(switched.HasValue, Is.True);
            Assert.That(switched.Value.Label, Is.EqualTo("banana"));
            Assert.That(switched.Value.Score, Is.EqualTo(0.89f).Within(1e-6f));
        }

        [Test]
        public void Observe_ClearsRecognitionOnlyAfterLossGracePeriodExpires()
        {
            var filter = new TemporalRecognitionFilter(Settings(requiredConsecutiveHits: 2, lossGracePeriodSeconds: 0.5f));

            filter.Observe(new RecognitionCandidate("apple", 0.90f), 0.0);
            filter.Observe(new RecognitionCandidate("apple", 0.93f), 0.1);

            var withinGrace = filter.Observe(null, 0.55);
            var afterGrace = filter.Observe(null, 0.61);

            Assert.That(withinGrace.HasValue, Is.True);
            Assert.That(withinGrace.Value.Label, Is.EqualTo("apple"));
            Assert.That(afterGrace, Is.Null);
        }

        private static RecognitionSettings Settings(
            int requiredConsecutiveHits = 2,
            float lossGracePeriodSeconds = 0.4f,
            float minimumRecognitionIntervalSeconds = 0.2f,
            float maximumRecognitionIntervalSeconds = 0.5f)
        {
            return new RecognitionSettings(
                similarityThreshold: 0.8f,
                ambiguityMargin: 0.05f,
                requiredConsecutiveHits: requiredConsecutiveHits,
                lossGracePeriodSeconds: lossGracePeriodSeconds,
                minimumRecognitionIntervalSeconds: minimumRecognitionIntervalSeconds,
                maximumRecognitionIntervalSeconds: maximumRecognitionIntervalSeconds);
        }
    }
}
