using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplatePreparationTests
    {
        private static byte[] Rgba(int width, int height, Func<int, int, (byte r, byte g, byte b)> shade)
        {
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var (r, g, b) = shade(x, y);
                    var i = ((y * width) + x) * 4;
                    pixels[i] = r;
                    pixels[i + 1] = g;
                    pixels[i + 2] = b;
                    pixels[i + 3] = 255;
                }
            }

            return pixels;
        }

        [Test]
        public void ToGrayscaleSquare_ProducesTheRequestedSize()
        {
            var rgba = Rgba(80, 40, (x, y) => (10, 10, 10));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 80, 40, 0.75f, 16);

            Assert.That(gray.Length, Is.EqualTo(16 * 16));
        }

        [Test]
        public void ToGrayscaleSquare_WeightsGreenMostLikeHumanLuminance()
        {
            var green = TemplatePreparation.ToGrayscaleSquare(
                Rgba(8, 8, (x, y) => (0, 255, 0)), 8, 8, 1f, 4);
            var blue = TemplatePreparation.ToGrayscaleSquare(
                Rgba(8, 8, (x, y) => (0, 0, 255)), 8, 8, 1f, 4);

            Assert.That(green[0], Is.GreaterThan(blue[0]));
        }

        [Test]
        public void ToGrayscaleSquare_KeepsTheCentreAndDropsTheEdges()
        {
            // Black frame with a white centre column; a 50% crop must come back all white.
            var rgba = Rgba(40, 40, (x, y) => (x >= 10 && x < 30)
                ? ((byte)255, (byte)255, (byte)255)
                : ((byte)0, (byte)0, (byte)0));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 40, 40, 0.5f, 8);

            foreach (var pixel in gray)
            {
                Assert.That(pixel, Is.GreaterThan(200));
            }
        }

        [Test]
        public void ToGrayscaleSquare_KeepsTheTopOfTheImageAtTheTopOfTheTemplate()
        {
            // Rows run downward in the source; the template keeps that order, and only
            // TemplateMatch flips the axis when it reports a position.
            var rgba = Rgba(16, 16, (x, y) => y < 8
                ? ((byte)0, (byte)0, (byte)0)
                : ((byte)255, (byte)255, (byte)255));

            var gray = TemplatePreparation.ToGrayscaleSquare(rgba, 16, 16, 1f, 8);

            Assert.That(gray[0], Is.LessThan(50), "first row is the top of the source");
            Assert.That(gray[gray.Length - 1], Is.GreaterThan(200));
        }

        [Test]
        public void CentreCrop_TakesTheMiddleFraction()
        {
            var gray = new byte[16 * 16];
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    gray[(y * 16) + x] = (byte)(x >= 4 && x < 12 && y >= 4 && y < 12 ? 200 : 10);
                }
            }

            var cropped = TemplatePreparation.CentreCrop(gray, 16, 0.5f);

            Assert.That(cropped.Length, Is.EqualTo(8 * 8));
            foreach (var pixel in cropped)
            {
                Assert.That(pixel, Is.EqualTo(200));
            }
        }

        [Test]
        public void ToGrayscaleSquare_ReadsThreeChannelPixelsToo()
        {
            // A JPEG decoded by Texture2D.LoadImage comes back RGB24, not RGBA32 - it has no
            // alpha to store. Registration snapshots are JPEGs, so three channels is not an
            // exotic case, it is the normal one for a seeded template.
            var rgb = new byte[8 * 8 * 3];
            for (var i = 0; i < 8 * 8; i++)
            {
                rgb[(i * 3) + 1] = 255;     // pure green
            }

            var gray = TemplatePreparation.ToGrayscaleSquare(rgb, 8, 8, 1f, 4, bytesPerPixel: 3);

            Assert.That(gray.Length, Is.EqualTo(16));
            Assert.That(gray[0], Is.EqualTo(149));   // 0.587 * 255
        }

        [Test]
        public void ToGrayscaleSquare_ReadsTheSameSceneIdenticallyAtThreeOrFourChannels()
        {
            var rgba = Rgba(16, 16, (x, y) => ((byte)(x * 15), (byte)(y * 15), (byte)40));
            var rgb = new byte[16 * 16 * 3];
            for (var i = 0; i < 16 * 16; i++)
            {
                rgb[i * 3] = rgba[i * 4];
                rgb[(i * 3) + 1] = rgba[(i * 4) + 1];
                rgb[(i * 3) + 2] = rgba[(i * 4) + 2];
            }

            var fromRgba = TemplatePreparation.ToGrayscaleSquare(rgba, 16, 16, 0.75f, 8);
            var fromRgb = TemplatePreparation.ToGrayscaleSquare(rgb, 16, 16, 0.75f, 8, bytesPerPixel: 3);

            Assert.That(fromRgb, Is.EqualTo(fromRgba));
        }

        [Test]
        public void ToGrayscaleSquare_RejectsAPixelBufferThatIsNotRgba()
        {
            Assert.Throws<ArgumentException>(() =>
                TemplatePreparation.ToGrayscaleSquare(new byte[10], 8, 8, 1f, 4));
        }
    }
}
