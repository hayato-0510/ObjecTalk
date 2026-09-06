using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class RegistrationProgressViewTests
    {
        private const int TotalViews = 3;

        [Test]
        public void For_BeforeTheFirstCapture_PromptsViewOneWithNoHintAndNoLitDots()
        {
            var view = RegistrationProgressView.For(0, TotalViews);

            Assert.That(view.Status, Is.EqualTo(RecognitionMessages.RegistrationViewPrompt(1, TotalViews)));
            Assert.That(view.Instruction, Is.Empty);
            Assert.That(view.LitDots, Is.EqualTo(0));
        }

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        public void For_MidSession_PromptsTheNextViewAndAddsTheAngleHint(int capturedViews, int expectedNextView)
        {
            var view = RegistrationProgressView.For(capturedViews, TotalViews);

            Assert.That(
                view.Status,
                Is.EqualTo(RecognitionMessages.RegistrationViewPrompt(expectedNextView, TotalViews)));
            Assert.That(view.Instruction, Is.EqualTo(RecognitionMessages.RegistrationAngleHint));
            Assert.That(view.LitDots, Is.EqualTo(capturedViews));
        }

        [Test]
        public void For_AllViewsCaptured_ReportsCompletionAndLightsEveryDot()
        {
            var view = RegistrationProgressView.For(TotalViews, TotalViews);

            Assert.That(view.Status, Is.EqualTo(RecognitionMessages.RegistrationComplete));
            Assert.That(view.LitDots, Is.EqualTo(TotalViews));
        }

        [Test]
        public void For_MoreCapturesThanRequired_ClampsTheLitDotsToTheTotal()
        {
            var view = RegistrationProgressView.For(TotalViews + 2, TotalViews);

            Assert.That(view.Status, Is.EqualTo(RecognitionMessages.RegistrationComplete));
            Assert.That(view.LitDots, Is.EqualTo(TotalViews));
        }

        [Test]
        public void For_RejectsANegativeCaptureCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RegistrationProgressView.For(-1, TotalViews));
        }

        [Test]
        public void For_RejectsATotalBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RegistrationProgressView.For(0, 0));
        }
    }
}
