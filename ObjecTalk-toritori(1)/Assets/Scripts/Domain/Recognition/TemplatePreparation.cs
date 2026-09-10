using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Turns camera or capture pixels into the square grayscale image <see cref="TemplateMatch"/>
    /// works on. Framing follows <see cref="ImageGeometry"/> so a template and a live view are
    /// cropped identically; anything else would compare two different framings.
    /// </summary>
    public static class TemplatePreparation
    {
        // Rec. 601 luma. Green dominates because human vision does.
        private const double RedWeight = 0.299d;
        private const double GreenWeight = 0.587d;
        private const double BlueWeight = 0.114d;

        /// <summary>
        /// Centre-crops <paramref name="rgba"/> to a square, converts to luma, and resamples to
        /// <paramref name="outputSize"/>. Row order is preserved: row zero is the top of the
        /// source, and only <see cref="TemplateMatch"/> flips the axis when reporting a position.
        /// </summary>
        public static byte[] ToGrayscaleSquare(
            ReadOnlySpan<byte> rgba, int width, int height, float cropFraction, int outputSize)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The image has no size.");
            }

            if (outputSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputSize), "The template has no size.");
            }

            if (rgba.Length != width * height * 4)
            {
                throw new ArgumentException(
                    "Pixels must be four channels per pixel at the declared size.", nameof(rgba));
            }

            var crop = ImageGeometry.CenterCrop(width, height, cropFraction);
            var output = new byte[outputSize * outputSize];

            for (var y = 0; y < outputSize; y++)
            {
                var sy = crop.Y + (y * crop.Height / outputSize);
                for (var x = 0; x < outputSize; x++)
                {
                    var sx = crop.X + (x * crop.Width / outputSize);
                    var i = ((sy * width) + sx) * 4;
                    var luma = (RedWeight * rgba[i]) + (GreenWeight * rgba[i + 1]) + (BlueWeight * rgba[i + 2]);
                    output[(y * outputSize) + x] = (byte)(luma > 255d ? 255d : luma);
                }
            }

            return output;
        }

        /// <summary>The middle <paramref name="fraction"/> of a square grayscale image.</summary>
        public static byte[] CentreCrop(ReadOnlySpan<byte> gray, int size, float fraction)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "The image has no size.");
            }

            if (gray.Length != size * size)
            {
                throw new ArgumentException(
                    "The pixel count does not match the declared size.", nameof(gray));
            }

            if (fraction <= 0f || fraction > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(fraction), "The fraction must be within (0, 1].");
            }

            var side = Math.Max(1, (int)(size * fraction));
            var offset = (size - side) / 2;
            var output = new byte[side * side];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    output[(y * side) + x] = gray[((offset + y) * size) + offset + x];
                }
            }

            return output;
        }
    }
}
