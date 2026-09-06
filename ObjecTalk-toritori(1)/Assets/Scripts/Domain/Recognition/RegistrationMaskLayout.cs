using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Lays out the opaque surround that hides everything on the registration preview except the
    /// centred capture square. Four non-overlapping strips (top, bottom, left, right) tile the
    /// preview minus a centred <c>holeSide</c> square. Engine-free maths so it is testable without a
    /// canvas (Ruling T2-1b — no UnityEngine types in this assembly); the controller copies each
    /// strip's <see cref="Strip.CenterX"/>/<see cref="Strip.CenterY"/> into
    /// <c>RectTransform.anchoredPosition</c> and <see cref="Strip.Width"/>/<see cref="Strip.Height"/>
    /// into <c>sizeDelta</c>, with the strips centre-anchored on the preview.
    /// </summary>
    public static class RegistrationMaskLayout
    {
        public readonly struct Strip
        {
            public Strip(float centerX, float centerY, float width, float height)
            {
                CenterX = centerX;
                CenterY = centerY;
                Width = width;
                Height = height;
            }

            public float CenterX { get; }
            public float CenterY { get; }
            public float Width { get; }
            public float Height { get; }
        }

        public readonly struct Edges
        {
            public Edges(Strip top, Strip bottom, Strip left, Strip right)
            {
                Top = top;
                Bottom = bottom;
                Left = left;
                Right = right;
            }

            public Strip Top { get; }
            public Strip Bottom { get; }
            public Strip Left { get; }
            public Strip Right { get; }
        }

        public static Edges EdgeRects(float previewWidth, float previewHeight, float holeSide)
        {
            if (previewWidth <= 0f || previewHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(previewWidth), "The preview has no size.");
            }

            var hole = Math.Min(holeSide, Math.Min(previewWidth, previewHeight));
            hole = Math.Max(0f, hole);

            var verticalGap = (previewHeight - hole) / 2f;   // height of the top / bottom strips
            var horizontalGap = (previewWidth - hole) / 2f;  // width of the left / right strips

            var verticalCenter = (previewHeight + hole) / 4f; // midpoint of that gap from the centre

            var top = new Strip(0f, verticalCenter, previewWidth, verticalGap);
            var bottom = new Strip(0f, -verticalCenter, previewWidth, verticalGap);

            var horizontalCenter = (previewWidth + hole) / 4f;

            // Left / right only fill the band level with the hole, so they never overlap top / bottom.
            var left = new Strip(-horizontalCenter, 0f, horizontalGap, hole);
            var right = new Strip(horizontalCenter, 0f, horizontalGap, hole);

            return new Edges(top, bottom, left, right);
        }
    }
}
