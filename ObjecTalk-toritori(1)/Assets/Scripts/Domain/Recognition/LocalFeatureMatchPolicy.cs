using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Decides whether a set of descriptor-match distances confirms the neural candidate.
    /// Kept free of OpenCV and Unity types so the verification rule stays testable.
    /// </summary>
    public static class LocalFeatureMatchPolicy
    {
        /// <summary>
        /// Hamming distance budget for AKAZE's binary descriptors; matches at or above this are noise.
        /// </summary>
        public const float DefaultMaximumDescriptorDistance = 50f;

        /// <summary>
        /// Number of finite, non-negative distances strictly below <paramref name="maximumDistance"/>.
        /// </summary>
        public static int CountGoodMatches(ReadOnlySpan<float> distances, float maximumDistance)
        {
            if (float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDistance),
                    "The descriptor distance budget must be finite and greater than zero.");
            }

            var count = 0;
            for (var i = 0; i < distances.Length; i++)
            {
                var distance = distances[i];
                if (distance >= 0f && distance < maximumDistance)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// True when at least <paramref name="minimumMatches"/> descriptors match closely enough.
        /// </summary>
        public static bool IsVerified(ReadOnlySpan<float> distances, float maximumDistance, int minimumMatches)
        {
            if (minimumMatches < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumMatches),
                    "Verification requires at least one matching descriptor.");
            }

            return CountGoodMatches(distances, maximumDistance) >= minimumMatches;
        }
    }
}
