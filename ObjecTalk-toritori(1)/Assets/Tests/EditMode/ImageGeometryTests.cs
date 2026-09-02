using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class ImageGeometryTests
    {
        [Test]
        public void CenterCrop_LandscapeKeepsFullHeightAndCentersHorizontally()
        {
            var crop = ImageGeometry.CenterCrop(640, 480);

            Assert.That(crop.X, Is.EqualTo(80));
            Assert.That(crop.Y, Is.EqualTo(0));
            Assert.That(crop.Width, Is.EqualTo(480));
            Assert.That(crop.Height, Is.EqualTo(480));
        }

        [Test]
        public void CenterCrop_PortraitKeepsFullWidthAndCentersVertically()
        {
            var crop = ImageGeometry.CenterCrop(480, 640);

            Assert.That(crop.X, Is.EqualTo(0));
            Assert.That(crop.Y, Is.EqualTo(80));
            Assert.That(crop.Width, Is.EqualTo(480));
            Assert.That(crop.Height, Is.EqualTo(480));
        }

        [Test]
        public void CenterCrop_SquareReturnsTheWholeImage()
        {
            var crop = ImageGeometry.CenterCrop(512, 512);

            Assert.That(crop.X, Is.EqualTo(0));
            Assert.That(crop.Y, Is.EqualTo(0));
            Assert.That(crop.Width, Is.EqualTo(512));
            Assert.That(crop.Height, Is.EqualTo(512));
        }

        [Test]
        public void CenterCrop_OddLandscapeStaysInsideTheSource()
        {
            var crop = ImageGeometry.CenterCrop(641, 481);

            Assert.That(crop.Width, Is.EqualTo(481));
            Assert.That(crop.Height, Is.EqualTo(481));
            Assert.That(crop.X, Is.EqualTo(80));
            Assert.That(crop.Y, Is.EqualTo(0));
            Assert.That(crop.X + crop.Width, Is.LessThanOrEqualTo(641));
            Assert.That(crop.Y + crop.Height, Is.LessThanOrEqualTo(481));
        }

        [Test]
        public void CenterCrop_OddPortraitStaysInsideTheSource()
        {
            var crop = ImageGeometry.CenterCrop(481, 641);

            Assert.That(crop.Width, Is.EqualTo(481));
            Assert.That(crop.Height, Is.EqualTo(481));
            Assert.That(crop.X, Is.EqualTo(0));
            Assert.That(crop.Y, Is.EqualTo(80));
            Assert.That(crop.X + crop.Width, Is.LessThanOrEqualTo(481));
            Assert.That(crop.Y + crop.Height, Is.LessThanOrEqualTo(641));
        }

        [Test]
        public void CenterCrop_IsAlwaysSquareAndInsideTheSource()
        {
            int[] widths = { 1, 2, 3, 17, 320, 639, 1920, 4032 };
            int[] heights = { 1, 5, 4, 16, 321, 640, 1080, 3024 };

            foreach (var width in widths)
            {
                foreach (var height in heights)
                {
                    var crop = ImageGeometry.CenterCrop(width, height);

                    Assert.That(crop.Width, Is.EqualTo(crop.Height), $"{width}x{height} is not square");
                    Assert.That(crop.Width, Is.EqualTo(Math.Min(width, height)), $"{width}x{height} has the wrong side");
                    Assert.That(crop.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(crop.Y, Is.GreaterThanOrEqualTo(0));
                    Assert.That(crop.X + crop.Width, Is.LessThanOrEqualTo(width), $"{width}x{height} overflows horizontally");
                    Assert.That(crop.Y + crop.Height, Is.LessThanOrEqualTo(height), $"{width}x{height} overflows vertically");
                }
            }
        }

        [Test]
        public void CenterCrop_RejectsNonPositiveDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.CenterCrop(0, 480));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.CenterCrop(640, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.CenterCrop(-640, 480));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.CenterCrop(640, -480));
        }

        [Test]
        public void NormalizedCenterCrop_LandscapeScalesOnlyTheHorizontalAxis()
        {
            var crop = ImageGeometry.NormalizedCenterCrop(640, 480);

            Assert.That(crop.ScaleX, Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(crop.ScaleY, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(crop.OffsetX, Is.EqualTo(0.125f).Within(1e-6f));
            Assert.That(crop.OffsetY, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void NormalizedCenterCrop_PortraitScalesOnlyTheVerticalAxis()
        {
            var crop = ImageGeometry.NormalizedCenterCrop(480, 640);

            Assert.That(crop.ScaleX, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(crop.ScaleY, Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(crop.OffsetX, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(crop.OffsetY, Is.EqualTo(0.125f).Within(1e-6f));
        }

        [Test]
        public void NormalizedCenterCrop_NeverSamplesOutsideTheSource()
        {
            int[] widths = { 3, 17, 320, 639, 1920 };
            int[] heights = { 4, 16, 321, 640, 1080 };

            foreach (var width in widths)
            {
                foreach (var height in heights)
                {
                    var crop = ImageGeometry.NormalizedCenterCrop(width, height);

                    Assert.That(crop.OffsetX, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(crop.OffsetY, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(crop.OffsetX + crop.ScaleX, Is.LessThanOrEqualTo(1f + 1e-6f));
                    Assert.That(crop.OffsetY + crop.ScaleY, Is.LessThanOrEqualTo(1f + 1e-6f));
                }
            }
        }

        [Test]
        public void NormalizedCenterCrop_RejectsNonPositiveDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.NormalizedCenterCrop(0, 480));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.NormalizedCenterCrop(640, -1));
        }

        [Test]
        public void VerificationImageSize_ClampsToTheMaximumWithoutUpscaling()
        {
            Assert.That(ImageGeometry.MaximumVerificationImageSize, Is.EqualTo(320));
            Assert.That(ImageGeometry.VerificationImageSize(480), Is.EqualTo(320));
            Assert.That(ImageGeometry.VerificationImageSize(320), Is.EqualTo(320));
            Assert.That(ImageGeometry.VerificationImageSize(240), Is.EqualTo(240));
            Assert.That(ImageGeometry.VerificationImageSize(1), Is.EqualTo(1));
        }

        [Test]
        public void VerificationImageSize_RejectsNonPositiveSizes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.VerificationImageSize(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageGeometry.VerificationImageSize(-320));
        }

        [Test]
        public void EmbeddingImageSize_MatchesTheExportedModelInput()
        {
            Assert.That(ImageGeometry.EmbeddingImageSize, Is.EqualTo(224));
        }
    }
}
