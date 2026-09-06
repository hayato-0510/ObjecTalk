using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class RegistrationGuideLayoutTests
    {
        [Test]
        public void FrameSize_IsTheFractionOfTheShortSide()
        {
            var side = RegistrationGuideLayout.FrameSize(1080f, 1920f, 0.75f);

            Assert.That(side, Is.EqualTo(810f).Within(0.01f)); // 0.75 * 1080
        }

        [Test]
        public void FrameSize_ClampsFractionToOne()
        {
            var side = RegistrationGuideLayout.FrameSize(600f, 800f, 2f);

            Assert.That(side, Is.EqualTo(600f).Within(0.01f));
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        public void FrameSize_WithNonPositivePreview_Throws(float previewWidth)
        {
            Assert.That(
                () => RegistrationGuideLayout.FrameSize(previewWidth, 800f, 0.75f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
