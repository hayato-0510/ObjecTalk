using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// How a raw camera frame is rotated and mirrored relative to an upright image. Pure data
    /// plus the corner-UV mapping used to draw the frame the right way up. Engine-free so the
    /// mapping can be asserted without a Unity player or a graphics device.
    /// </summary>
    public readonly struct FrameOrientation : IEquatable<FrameOrientation>
    {
        private FrameOrientation(int rotationDegrees, bool mirrored)
        {
            RotationDegrees = rotationDegrees;
            Mirrored = mirrored;
        }

        /// <summary>Clockwise rotation the source frame carries, always one of 0, 90, 180, 270.</summary>
        public int RotationDegrees { get; }

        /// <summary>Whether the source frame is also flipped horizontally.</summary>
        public bool Mirrored { get; }

        /// <summary>True when the frame is already upright and un-mirrored.</summary>
        public bool IsIdentity => RotationDegrees == 0 && !Mirrored;

        /// <summary>
        /// Builds an orientation from <c>WebCamTexture.videoRotationAngle</c> /
        /// <c>videoVerticallyMirrored</c>, snapping any integer angle (negative, &gt;360) to the
        /// nearest quarter turn.
        /// </summary>
        public static FrameOrientation FromWebCam(int videoRotationAngle, bool videoVerticallyMirrored)
        {
            var normalized = ((videoRotationAngle % 360) + 360) % 360;
            var quarter = (int)Math.Round(normalized / 90f, MidpointRounding.AwayFromZero) % 4;
            return new FrameOrientation(quarter * 90, videoVerticallyMirrored);
        }

        /// <summary>
        /// Size of the upright image: width and height are swapped for the quarter turns
        /// (90 and 270), unchanged for 0 and 180.
        /// </summary>
        public (int width, int height) OrientedSize(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "The source frame has no pixels.");
            }

            return RotationDegrees == 90 || RotationDegrees == 270
                ? (sourceHeight, sourceWidth)
                : (sourceWidth, sourceHeight);
        }

        /// <summary>
        /// Source UVs to sample for the oriented (upright) quad's corners, in the order
        /// bottom-left, bottom-right, top-right, top-left. Feed these to a textured quad drawn
        /// over the unit rectangle to produce an upright, un-mirrored frame. Returned as plain
        /// <c>(u, v)</c> pairs so the domain assembly stays engine-free.
        /// </summary>
        /// <remarks>
        /// The base corners are the unit square in BL, BR, TR, TL order. Undoing a clockwise
        /// source rotation of <c>RotationDegrees</c> means rotating the sample points counter-
        /// clockwise, i.e. reading the base list starting <c>shift</c> entries earlier, where
        /// <c>shift = (4 - RotationDegrees / 90) % 4</c>. For identity that is <c>shift == 0</c>
        /// (the unit square, unchanged); for 90 degrees <c>shift == 3</c>, which sends the
        /// oriented bottom-left corner to the source's top-left <c>(0, 1)</c>. Mirroring then
        /// reflects every sample's u about 0.5.
        /// </remarks>
        public (float u, float v)[] SourceUVsForOrientedCorners()
        {
            var uv = new[]
            {
                (u: 0f, v: 0f), // BL
                (u: 1f, v: 0f), // BR
                (u: 1f, v: 1f), // TR
                (u: 0f, v: 1f), // TL
            };

            var shift = (4 - RotationDegrees / 90) % 4;
            var rotated = new (float u, float v)[4];
            for (var i = 0; i < 4; i++)
            {
                rotated[i] = uv[(i + shift) % 4];
            }

            if (Mirrored)
            {
                for (var i = 0; i < 4; i++)
                {
                    rotated[i] = (1f - rotated[i].u, rotated[i].v);
                }
            }

            return rotated;
        }

        public bool Equals(FrameOrientation other) =>
            RotationDegrees == other.RotationDegrees && Mirrored == other.Mirrored;

        public override bool Equals(object obj) => obj is FrameOrientation other && Equals(other);

        public override int GetHashCode() => (RotationDegrees * 397) ^ (Mirrored ? 1 : 0);

        public override string ToString() => $"FrameOrientation({RotationDegrees}, mirrored={Mirrored})";
    }
}
