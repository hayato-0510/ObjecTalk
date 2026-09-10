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
        /// Centre-crops <paramref name="pixels"/> to a square, converts to luma, and resamples to
        /// <paramref name="outputSize"/>. Row order is preserved: row zero is the top of the
        /// source, and only <see cref="TemplateMatch"/> flips the axis when reporting a position.
        ///
        /// <paramref name="bytesPerPixel"/> is four for RGBA32 and three for RGB24. Both turn up:
        /// a camera frame carries alpha, a JPEG decoded from a registration snapshot does not.
        /// </summary>
        public static byte[] ToGrayscaleSquare(
            ReadOnlySpan<byte> pixels,
            int width,
            int height,
            float cropFraction,
            int outputSize,
            int bytesPerPixel = 4)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The image has no size.");
            }

            if (outputSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputSize), "The template has no size.");
            }

            if (bytesPerPixel != 3 && bytesPerPixel != 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bytesPerPixel), "Only three- and four-channel pixels are supported.");
            }

            if (pixels.Length != width * height * bytesPerPixel)
            {
                throw new ArgumentException(
                    "The pixel count does not match the declared size and channel count.", nameof(pixels));
            }

            var crop = ImageGeometry.CenterCrop(width, height, cropFraction);
            var output = new byte[outputSize * outputSize];

            for (var y = 0; y < outputSize; y++)
            {
                var sy = crop.Y + (y * crop.Height / outputSize);
                for (var x = 0; x < outputSize; x++)
                {
                    var sx = crop.X + (x * crop.Width / outputSize);
                    var i = ((sy * width) + sx) * bytesPerPixel;
                    var luma = (RedWeight * pixels[i])
                        + (GreenWeight * pixels[i + 1])
                        + (BlueWeight * pixels[i + 2]);
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
