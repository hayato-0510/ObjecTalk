using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class FaceOverlayLayoutTests
    {
        [Test]
        public void AnchoredPosition_MapsTheCentreToTheOrigin()
        {
            var (x, y) = FaceOverlayLayout.AnchoredPosition(0.5f, 0.5f, 800f, 600f);

            Assert.That(x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void AnchoredPosition_MapsRelativeCoordsFromTheBottomLeft()
        {
            // Bottom-left origin, centre-anchored image: (0,0) -> (-w/2, -h/2), (1,1) -> (+w/2, +h/2).
            var bottomLeft = FaceOverlayLayout.AnchoredPosition(0f, 0f, 800f, 600f);
            Assert.That(bottomLeft.x, Is.EqualTo(-400f).Within(0.01f));
            Assert.That(bottomLeft.y, Is.EqualTo(-300f).Within(0.01f));

            var topRight = FaceOverlayLayout.AnchoredPosition(1f, 1f, 800f, 600f);
            Assert.That(topRight.x, Is.EqualTo(400f).Within(0.01f));
            Assert.That(topRight.y, Is.EqualTo(300f).Within(0.01f));
        }

        [Test]
        public void AnchoredPosition_ClampsRelativeCoordsToTheRect()
        {
            var (x, y) = FaceOverlayLayout.AnchoredPosition(2f, -1f, 800f, 600f);

            Assert.That(x, Is.EqualTo(400f).Within(0.01f));
            Assert.That(y, Is.EqualTo(-300f).Within(0.01f));
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        public void AnchoredPosition_WithNonPositiveRect_Throws(float rectWidth)
        {
            Assert.That(
                () => FaceOverlayLayout.AnchoredPosition(0.5f, 0.5f, rectWidth, 600f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
