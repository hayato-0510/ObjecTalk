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

            Assert.That(scheduler.TryBegin(0.0), Is.True);
            Assert.That(scheduler.TryBegin(0.1), Is.False);
        }

        [Test]
        public void Complete_UsesClampedMinimumIntervalForFastWork()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings(minimumRecognitionIntervalSeconds: 0.05f));

            Assert.That(scheduler.TryBegin(0.0), Is.True);

            scheduler.Complete(nowSeconds: 0.1, elapsedSeconds: 0.05);

            Assert.That(scheduler.CurrentIntervalSeconds, Is.EqualTo(0.2).Within(1e-6));
            Assert.That(scheduler.TryBegin(0.29), Is.False);
            Assert.That(scheduler.TryBegin(0.300001), Is.True);
        }

        [Test]
        public void Complete_UsesClampedMaximumIntervalForSlowWork()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings(maximumRecognitionIntervalSeconds: 0.8f));

            Assert.That(scheduler.TryBegin(0.0), Is.True);

            scheduler.Complete(nowSeconds: 0.1, elapsedSeconds: 0.9);

            Assert.That(scheduler.CurrentIntervalSeconds, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(scheduler.TryBegin(0.59), Is.False);
            Assert.That(scheduler.TryBegin(0.600001), Is.True);
        }

        [Test]
        public void Cancel_BumpsGenerationAndLeavesOldCompletionStale()
        {
            var scheduler = new AdaptiveRecognitionScheduler(Settings());

            Assert.That(scheduler.TryBegin(0.0), Is.True);
            var cancelledGeneration = scheduler.Generation;

            scheduler.Cancel();

            Assert.That(scheduler.IsBusy, Is.False);
            Assert.That(scheduler.Generation, Is.GreaterThan(cancelledGeneration));
            Assert.That(scheduler.TryBegin(0.0), Is.True);

            var activeGeneration = scheduler.Generation;

            if (cancelledGeneration == scheduler.Generation)
            {
                scheduler.Complete(nowSeconds: 0.1, elapsedSeconds: 0.1);
            }

            Assert.That(scheduler.Generation, Is.EqualTo(activeGeneration));
            Assert.That(scheduler.IsBusy, Is.True);
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
