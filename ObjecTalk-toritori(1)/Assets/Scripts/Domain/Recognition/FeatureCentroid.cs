using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Turns the keypoints that matched a registered object into that object's position in the
    /// live frame. Kept free of OpenCV and Unity types for the same reason as
    /// <see cref="LocalFeatureMatchPolicy"/> - the rule stays testable without an editor.
    /// </summary>
    public static class FeatureCentroid
    {
        /// <summary>
        /// How many median absolute deviations a point may sit from the median before it counts as
        /// a stray match rather than part of the object.
        /// </summary>
        public const float OutlierDistanceMultiplier = 3f;

        /// <summary>
        /// Locates the matched cluster and reports it as a 0..1 position with a bottom-left origin,
        /// the convention <see cref="FaceOverlayLayout"/> expects. Incoming pixel coordinates use
        /// OpenCV's top-left origin, so the vertical axis is flipped here.
        /// Returns false when there are too few points to trust.
        /// </summary>
        public static bool TryCompute(
            ReadOnlySpan<float> pixelXs,
            ReadOnlySpan<float> pixelYs,
            int imageWidth,
            int imageHeight,
            int minimumPoints,
            out float relativeX,
            out float relativeY)
        {
            relativeX = 0f;
            relativeY = 0f;

            if (pixelXs.Length != pixelYs.Length)
            {
                throw new ArgumentException(
                    "Each matched point needs both coordinates.", nameof(pixelYs));
            }

            if (imageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageWidth), "The frame has no size.");
            }

            if (imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageHeight), "The frame has no size.");
            }

            if (minimumPoints < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumPoints), "Locating an object needs at least one point.");
            }

            var count = pixelXs.Length;
            if (count < minimumPoints)
            {
                return false;
            }

            var scratch = new float[count];
            var medianX = MedianOf(pixelXs, scratch);
            var medianY = MedianOf(pixelYs, scratch);

            var distances = new float[count];
            for (var i = 0; i < count; i++)
            {
                var dx = pixelXs[i] - medianX;
                var dy = pixelYs[i] - medianY;
                distances[i] = (float)Math.Sqrt((dx * dx) + (dy * dy));
            }

            var threshold = OutlierDistanceMultiplier * MedianOf(distances, scratch);

            // Averaging the survivors rather than taking the median outright keeps the result
            // sub-pixel smooth; a bare median snaps between discrete keypoints and reads as jitter.
            double sumX = 0d;
            double sumY = 0d;
            var kept = 0;
            for (var i = 0; i < count; i++)
            {
                if (distances[i] > threshold)
                {
                    continue;
                }

                sumX += pixelXs[i];
                sumY += pixelYs[i];
                kept++;
            }

            if (kept == 0)
            {
                return false;
            }

            relativeX = Clamp01((float)(sumX / kept) / imageWidth);
            relativeY = Clamp01(1f - ((float)(sumY / kept) / imageHeight));
            return true;
        }

        private static float MedianOf(ReadOnlySpan<float> values, float[] scratch)
        {
            values.CopyTo(scratch);
            Array.Sort(scratch, 0, values.Length);

            var middle = values.Length / 2;
            return values.Length % 2 == 1
                ? scratch[middle]
                : 0.5f * (scratch[middle - 1] + scratch[middle]);
        }

        private static float Clamp01(float value)
            => value <= 0f ? 0f : (value >= 1f ? 1f : value);
    }
}
