using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Engine-free maths for animating the recognition face overlay: turning a chunk of playback
    /// PCM into a "mouth openness" 0..1, and that openness into a vertical scale for the mouth
    /// image. No UnityEngine types so it stays unit-testable (same rule as
    /// <see cref="FaceOverlayLayout"/>).
    /// </summary>
    public static class FaceOverlayMotion
    {
        /// <summary>Root-mean-square level of PCM samples that sit in roughly [-1, 1].</summary>
        public static float Rms(float[] samples, int count)
        {
            if (samples == null || count <= 0)
            {
                return 0f;
            }

            int n = Math.Min(count, samples.Length);
            double sum = 0d;
            for (int i = 0; i < n; i++)
            {
                double s = samples[i];
                sum += s * s;
            }

            return (float)Math.Sqrt(sum / n);
        }

        /// <summary>
        /// Map an RMS level to mouth openness 0..1. Everything below <paramref name="noiseFloor"/>
        /// counts as closed; <paramref name="gain"/> scales the rest before clamping.
        /// </summary>
        public static float MouthOpenness(float rms, float gain, float noiseFloor)
        {
            if (gain <= 0f || rms <= noiseFloor)
            {
                return 0f;
            }

            float value = (rms - noiseFloor) * gain;
            return value <= 0f ? 0f : (value >= 1f ? 1f : value);
        }

        /// <summary>Vertical scale for the mouth image given openness 0..1 and its closed/open bounds.</summary>
        public static float MouthScaleY(float openness, float closedScaleY, float openScaleY)
        {
            float t = openness <= 0f ? 0f : (openness >= 1f ? 1f : openness);
            return closedScaleY + (openScaleY - closedScaleY) * t;
        }
    }
}
