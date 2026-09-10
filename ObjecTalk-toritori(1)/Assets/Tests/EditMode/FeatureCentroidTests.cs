using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class FeatureCentroidTests
    {
        [Test]
        public void TryCompute_ReturnsTheClusterCentreWithTheOriginFlippedToBottomLeft()
        {
            // A tight cluster around (80, 240) in a 320x320 frame. OpenCV's origin is top-left,
            // so y=240 must come back as 0.25 from the bottom.
            var xs = new[] { 76f, 80f, 84f };
            var ys = new[] { 236f, 240f, 244f };

            var located = FeatureCentroid.TryCompute(
                xs, ys, imageWidth: 320, imageHeight: 320, minimumPoints: 3,
                out var relativeX, out var relativeY);

            Assert.That(located, Is.True);
            Assert.That(relativeX, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(relativeY, Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void TryCompute_DropsAPointFarOutsideTheCluster()
        {
            // Four points around (101, 101) plus one stray match at (300, 300). The stray sits far
            // beyond three median absolute deviations and must not drag the centre toward it.
            var xs = new[] { 100f, 102f, 100f, 102f, 300f };
            var ys = new[] { 100f, 100f, 102f, 102f, 300f };

            var located = FeatureCentroid.TryCompute(
                xs, ys, imageWidth: 400, imageHeight: 400, minimumPoints: 4,
                out var relativeX, out var relativeY);

            Assert.That(located, Is.True);
            Assert.That(relativeX, Is.EqualTo(101f / 400f).Within(1e-4f));
            Assert.That(relativeY, Is.EqualTo(1f - (101f / 400f)).Within(1e-4f));
        }

        [Test]
        public void TryCompute_RejectsFewerPointsThanTheMinimum()
        {
            var xs = new[] { 10f, 12f };
            var ys = new[] { 10f, 12f };

            var located = FeatureCentroid.TryCompute(
                xs, ys, imageWidth: 320, imageHeight: 320, minimumPoints: 3,
                out var relativeX, out var relativeY);

            Assert.That(located, Is.False);
            Assert.That(relativeX, Is.EqualTo(0f));
            Assert.That(relativeY, Is.EqualTo(0f));
        }

        [Test]
        public void TryCompute_RejectsAnEmptySet()
        {
            var located = FeatureCentroid.TryCompute(
                Array.Empty<float>(), Array.Empty<float>(),
                imageWidth: 320, imageHeight: 320, minimumPoints: 1,
                out _, out _);

            Assert.That(located, Is.False);
        }

        [Test]
        public void TryCompute_RejectsMismatchedCoordinateCounts()
        {
            Assert.Throws<ArgumentException>(() => FeatureCentroid.TryCompute(
                new[] { 1f, 2f }, new[] { 1f },
                imageWidth: 320, imageHeight: 320, minimumPoints: 1,
                out _, out _));
        }

        [Test]
        public void TryCompute_RejectsAFrameWithNoSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FeatureCentroid.TryCompute(
                new[] { 1f }, new[] { 1f },
                imageWidth: 0, imageHeight: 320, minimumPoints: 1,
                out _, out _));
        }
    }
}
