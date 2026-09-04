using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Pixel rectangle expressed with a bottom-left origin, matching Unity texture coordinates.
    /// </summary>
    [Serializable]
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public PixelRect(int x, int y, int width, int height)
        {
            if (x < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Crop rectangles cannot start before the source.");
            }

            if (y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(y), "Crop rectangles cannot start before the source.");
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangles require a positive width.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Crop rectangles require a positive height.");
            }

            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public bool Equals(PixelRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is PixelRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Width;
                hash = (hash * 397) ^ Height;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Width}x{Height})";
        }

        public static bool operator ==(PixelRect left, PixelRect right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PixelRect left, PixelRect right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Crop rectangle in normalized source coordinates, shaped for a scale/offset blit.
    /// </summary>
    [Serializable]
    public readonly struct NormalizedRect
    {
        public NormalizedRect(float scaleX, float scaleY, float offsetX, float offsetY)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public float ScaleX { get; }

        public float ScaleY { get; }

        public float OffsetX { get; }

        public float OffsetY { get; }
    }

    /// <summary>
    /// Aspect-preserving geometry shared by camera preprocessing and local-feature verification.
    /// Pure arithmetic so it stays testable outside the Unity player.
    /// </summary>
    public static class ImageGeometry
    {
        /// <summary>Square input side required by the exported embedding model.</summary>
        public const int EmbeddingImageSize = 224;

        /// <summary>Fraction of the short side kept by the registration/recognition center crop.</summary>
        public const float DefaultCropFraction = 0.75f;

        /// <summary>Upper bound for the CPU-readable image handed to AKAZE.</summary>
        public const int MaximumVerificationImageSize = 320;

        /// <summary>
        /// Largest centered square that fits inside the source, so resizing never stretches the frame.
        /// </summary>
        public static PixelRect CenterCrop(int sourceWidth, int sourceHeight)
        {
            ValidateSourceSize(sourceWidth, sourceHeight);

            var side = Math.Min(sourceWidth, sourceHeight);
            return new PixelRect((sourceWidth - side) / 2, (sourceHeight - side) / 2, side, side);
        }

        /// <summary>
        /// <see cref="CenterCrop"/> expressed as the scale and offset of a normalized blit.
        /// </summary>
        public static NormalizedRect NormalizedCenterCrop(int sourceWidth, int sourceHeight)
        {
            var crop = CenterCrop(sourceWidth, sourceHeight);
            return new NormalizedRect(
                crop.Width / (float)sourceWidth,
                crop.Height / (float)sourceHeight,
                crop.X / (float)sourceWidth,
                crop.Y / (float)sourceHeight);
        }

        /// <summary>
        /// Centered square covering <paramref name="cropFraction"/> of the source's short side.
        /// <paramref name="cropFraction"/> must be in the range (0, 1]; a value of 1 matches
        /// <see cref="CenterCrop(int, int)"/>.
        /// </summary>
        public static PixelRect CenterCrop(int sourceWidth, int sourceHeight, float cropFraction)
        {
            ValidateSourceSize(sourceWidth, sourceHeight);
            if (cropFraction <= 0f || cropFraction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cropFraction), "The crop fraction must be in the range (0, 1].");
            }

            var shortSide = Math.Min(sourceWidth, sourceHeight);
            var side = (int)Math.Round(shortSide * cropFraction, MidpointRounding.AwayFromZero);
            side = Math.Max(1, Math.Min(side, shortSide));
            return new PixelRect((sourceWidth - side) / 2, (sourceHeight - side) / 2, side, side);
        }

        /// <summary>
        /// <see cref="CenterCrop(int, int, float)"/> expressed as the scale and offset of a normalized blit.
        /// </summary>
        public static NormalizedRect NormalizedCenterCrop(int sourceWidth, int sourceHeight, float cropFraction)
        {
            var crop = CenterCrop(sourceWidth, sourceHeight, cropFraction);
            return new NormalizedRect(
                crop.Width / (float)sourceWidth,
                crop.Height / (float)sourceHeight,
                crop.X / (float)sourceWidth,
                crop.Y / (float)sourceHeight);
        }

        /// <summary>
        /// Side of the verification image: the cropped square, capped so AKAZE never sees more than
        /// <see cref="MaximumVerificationImageSize"/> pixels and never an upscaled frame.
        /// </summary>
        public static int VerificationImageSize(int croppedSize)
        {
            if (croppedSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(croppedSize), "Cropped sizes must be positive.");
            }

            return Math.Min(croppedSize, MaximumVerificationImageSize);
        }

        private static void ValidateSourceSize(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source images require a positive width.");
            }

            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight), "Source images require a positive height.");
            }
        }
    }
}
