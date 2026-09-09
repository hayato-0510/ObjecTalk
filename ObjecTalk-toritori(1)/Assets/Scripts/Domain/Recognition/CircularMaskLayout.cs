using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Normalized radius of the circular keep-region used to blank out background pixels around the
    /// registration/recognition crop, so the embedding and AKAZE both see the same object-only disc
    /// instead of the full (often background-filled) square. Engine-free maths (Ruling T2-1b) so it
    /// is testable without a canvas or a render texture; <see cref="CameraFramePreprocessor"/> feeds
    /// the result straight into the mask shader as a UV-space radius.
    /// </summary>
    public static class CircularMaskLayout
    {
        /// <summary>
        /// The largest radius that fits entirely inside a unit square, i.e. the incircle radius in
        /// normalized UV space (distance from the centre (0.5, 0.5) to the nearest edge).
        /// </summary>
        public const float MaxNormalizedRadius = 0.5f;

        /// <summary>
        /// Converts a mask fraction into the normalized UV radius the mask shader should keep
        /// un-blanked. <paramref name="maskFraction"/> is clamped to [0, 1]: 1 keeps the whole
        /// incircle (only the square's corners are blanked); smaller values shrink the kept circle
        /// and blank out more of the surrounding background.
        /// </summary>
        public static float NormalizedRadius(float maskFraction)
        {
            var fraction = Math.Min(1f, Math.Max(0f, maskFraction));
            return MaxNormalizedRadius * fraction;
        }
    }
}
