using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Side length of the on-preview capture guide: a centered square matching the fraction of the
    /// camera frame the embedding actually keeps. Engine-free maths so it is testable without a
    /// canvas (Ruling T2-1b — no UnityEngine types in this assembly).
    /// </summary>
    public static class RegistrationGuideLayout
    {
        public static float FrameSize(float previewWidth, float previewHeight, float cropFraction)
        {
            if (previewWidth <= 0f || previewHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(previewWidth), "The preview has no size.");
            }

            var fraction = Math.Min(1f, Math.Max(0f, cropFraction));
            return Math.Min(previewWidth, previewHeight) * fraction;
        }
    }
}
