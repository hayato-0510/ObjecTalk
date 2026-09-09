using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class CircularMaskLayoutTests
    {
        [Test]
        public void NormalizedRadius_AtFullFraction_MatchesTheIncircle()
        {
            var radius = CircularMaskLayout.NormalizedRadius(1f);

            Assert.That(radius, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void NormalizedRadius_ScalesLinearlyWithFraction()
        {
            var radius = CircularMaskLayout.NormalizedRadius(0.8f);

            Assert.That(radius, Is.EqualTo(0.4f).Within(0.0001f)); // 0.5 * 0.8
        }

        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(2f, 0.5f)]
        public void NormalizedRadius_ClampsFractionToZeroToOne(float fraction, float expected)
        {
            var radius = CircularMaskLayout.NormalizedRadius(fraction);

            Assert.That(radius, Is.EqualTo(expected).Within(0.0001f));
        }
    }
}
