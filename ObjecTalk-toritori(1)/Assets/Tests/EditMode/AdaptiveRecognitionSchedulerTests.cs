using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class AdaptiveRecognitionSchedulerTests
    {
        [Test]
        public void TryBegin_RejectsOverlapWhileBusy()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings());

            Assert.That(scheduler.TryBegin(0.0).HasValue, Is.True);
            Assert.That(scheduler.TryBegin(0.1), Is.Null);
        }

        [Test]
        public void Complete_UsesClampedMinimumIntervalForFastWork()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings(minimumRecognitionIntervalSeconds: 0.05f));
            var generation = scheduler.TryBegin(0.0);

            Assert.That(generation.HasValue, Is.True);

            Assert.That(scheduler.Complete(generation.Value, nowSeconds: 0.1, elapsedSeconds: 0.05), Is.True);

            Assert.That(scheduler.CurrentIntervalSeconds, Is.EqualTo(0.2).Within(1e-6));
            Assert.That(scheduler.TryBegin(0.29), Is.Null);
            Assert.That(scheduler.TryBegin(0.300001).HasValue, Is.True);
        }

        [Test]
        public void Complete_UsesClampedMaximumIntervalForSlowWork()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings(maximumRecognitionIntervalSeconds: 0.8f));
            var generation = scheduler.TryBegin(0.0);

            Assert.That(generation.HasValue, Is.True);

            Assert.That(scheduler.Complete(generation.Value, nowSeconds: 0.1, elapsedSeconds: 0.9), Is.True);

            Assert.That(scheduler.CurrentIntervalSeconds, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(scheduler.TryBegin(0.59), Is.Null);
            Assert.That(scheduler.TryBegin(0.600001).HasValue, Is.True);
        }

        [Test]
        public void Complete_IgnoresStaleTokenAfterCancelAndNewBegin()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings());
            var staleGeneration = scheduler.TryBegin(0.0);

            Assert.That(staleGeneration.HasValue, Is.True);

            scheduler.Cancel();

            Assert.That(scheduler.IsBusy, Is.False);
            var intervalBeforeStaleCompletion = scheduler.CurrentIntervalSeconds;
            var nextAllowedBeforeStaleCompletion = scheduler.NextAllowedTimeSeconds;
            var activeGeneration = scheduler.TryBegin(0.0);

            Assert.That(activeGeneration.HasValue, Is.True);
            Assert.That(activeGeneration.Value, Is.GreaterThan(staleGeneration.Value));
            Assert.That(scheduler.IsBusy, Is.True);

            Assert.That(scheduler.Complete(staleGeneration.Value, nowSeconds: 0.4, elapsedSeconds: 0.4), Is.False);

            Assert.That(scheduler.Generation, Is.EqualTo(activeGeneration.Value));
            Assert.That(scheduler.IsBusy, Is.True);
            Assert.That(scheduler.CurrentIntervalSeconds, Is.EqualTo(intervalBeforeStaleCompletion).Within(1e-6));
            Assert.That(scheduler.NextAllowedTimeSeconds, Is.EqualTo(nextAllowedBeforeStaleCompletion).Within(1e-9));
        }

        private static RecognitionSettings Settings(
            float minimumRecognitionIntervalSeconds = 0.2f,
            float maximumRecognitionIntervalSeconds = 0.5f)
        {
            return new RecognitionSettings(
                similarityThreshold: 0.8f,
                ambiguityMargin: 0.05f,
                requiredConsecutiveHits: 2,
                lossGracePeriodSeconds: 0.4f,
                minimumRecognitionIntervalSeconds: minimumRecognitionIntervalSeconds,
                maximumRecognitionIntervalSeconds: maximumRecognitionIntervalSeconds);
        }
    }
}
