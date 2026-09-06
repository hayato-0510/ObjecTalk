using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Maps a registered feature's relative position (0..1, bottom-left origin — the same convention
    /// <c>EmbeddingObjectMatcher</c> stores it in) onto an anchored position for a centre-anchored,
    /// centre-pivoted overlay image on the recognition preview. Engine-free maths so it is testable
    /// without a canvas (Ruling T2-1b — no UnityEngine types in this assembly).
    /// </summary>
    public static class FaceOverlayLayout
    {
        public static (float x, float y) AnchoredPosition(
            float relativeX, float relativeY, float rectWidth, float rectHeight)
        {
            if (rectWidth <= 0f || rectHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(rectWidth), "The preview rect has no size.");
            }

            var rx = Math.Min(1f, Math.Max(0f, relativeX));
            var ry = Math.Min(1f, Math.Max(0f, relativeY));

            return ((rx - 0.5f) * rectWidth, (ry - 0.5f) * rectHeight);
        }
    }
}
