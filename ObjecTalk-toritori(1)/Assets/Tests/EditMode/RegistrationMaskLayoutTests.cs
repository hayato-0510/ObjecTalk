using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class RegistrationMaskLayoutTests
    {
        [Test]
        public void EdgeRects_TileThePreviewMinusTheCentredHole()
        {
            // 100 x 200 preview, 40 x 40 hole in the centre.
            var edges = RegistrationMaskLayout.EdgeRects(100f, 200f, 40f);

            // Top and bottom span the full width and the gap above / below the hole.
            Assert.That(edges.Top.Width, Is.EqualTo(100f).Within(0.01f));
            Assert.That(edges.Top.Height, Is.EqualTo(80f).Within(0.01f));      // (200 - 40) / 2
            Assert.That(edges.Top.CenterX, Is.EqualTo(0f).Within(0.01f));
            Assert.That(edges.Top.CenterY, Is.EqualTo(60f).Within(0.01f));     // (200 + 40) / 4

            Assert.That(edges.Bottom.CenterY, Is.EqualTo(-60f).Within(0.01f));

            // Left and right fill only the band beside the hole so they do not overlap top / bottom.
            Assert.That(edges.Left.Width, Is.EqualTo(30f).Within(0.01f));      // (100 - 40) / 2
            Assert.That(edges.Left.Height, Is.EqualTo(40f).Within(0.01f));     // hole side
            Assert.That(edges.Left.CenterX, Is.EqualTo(-35f).Within(0.01f));   // -(100 + 40) / 4
            Assert.That(edges.Left.CenterY, Is.EqualTo(0f).Within(0.01f));

            Assert.That(edges.Right.CenterX, Is.EqualTo(35f).Within(0.01f));
        }

        [Test]
        public void EdgeRects_WithHoleFillingTheShortSide_HaveNoThickness()
        {
            var edges = RegistrationMaskLayout.EdgeRects(100f, 200f, 100f);

            Assert.That(edges.Left.Width, Is.EqualTo(0f).Within(0.01f));
            Assert.That(edges.Right.Width, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void EdgeRects_ClampANegativeGapToZero()
        {
            // Hole larger than the preview: no strip may report a negative size.
            var edges = RegistrationMaskLayout.EdgeRects(100f, 200f, 400f);

            Assert.That(edges.Top.Height, Is.GreaterThanOrEqualTo(0f));
            Assert.That(edges.Left.Width, Is.GreaterThanOrEqualTo(0f));
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        public void EdgeRects_WithNonPositivePreview_Throws(float previewWidth)
        {
            Assert.That(
                () => RegistrationMaskLayout.EdgeRects(previewWidth, 200f, 40f),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
