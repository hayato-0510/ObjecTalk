using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.Vision;
using UnityEngine;
using UnityEngine.TestTools.Constraints;

// Brings in AllocatingGCMemory() while keeping every other constraint on NUnit's Is.
using Is = NUnit.Framework.Is;

namespace ObjecTalk.Tests.PlayMode
{
    /// <summary>
    /// The source frames used here are built from a coloured centre square surrounded by a
    /// contrasting border. Any stretch, wrong crop, or flipped axis moves the border or the
    /// quadrants into positions these tests reject.
    /// </summary>
    public class CameraFramePreprocessorTests
    {
        private static readonly Color32 Border = new Color32(255, 0, 0, 255);
        private static readonly Color32 BottomLeft = new Color32(0, 255, 0, 255);
        private static readonly Color32 BottomRight = new Color32(0, 0, 255, 255);
        private static readonly Color32 TopLeft = new Color32(255, 255, 255, 255);
        private static readonly Color32 TopRight = new Color32(0, 0, 0, 255);

        private CameraFramePreprocessor _preprocessor;
        private Texture2D _source;
        private Texture2D _readback;

        [SetUp]
        public void SetUp()
        {
            _preprocessor = new CameraFramePreprocessor();
        }

        [TearDown]
        public void TearDown()
        {
            _preprocessor?.Dispose();
            _preprocessor = null;
            Destroy(ref _source);
            Destroy(ref _readback);
        }

        [Test]
        public void PrepareEmbedding_ProducesTheSquareModelInput()
        {
            _source = CreateSource(640, 480);

            var prepared = _preprocessor.PrepareEmbedding(_source);

            Assert.That(prepared, Is.Not.Null);
            Assert.That(prepared.width, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
            Assert.That(prepared.height, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
        }

        [Test]
        public void PrepareEmbedding_CentreCropsLandscapeWithoutStretching()
        {
            _source = CreateSource(640, 480);

            _readback = ReadBack(_preprocessor.PrepareEmbedding(_source));

            AssertQuadrantsAndNoBorder(_readback);
        }

        [Test]
        public void PrepareEmbedding_CentreCropsPortraitWithoutStretching()
        {
            _source = CreateSource(480, 640);

            _readback = ReadBack(_preprocessor.PrepareEmbedding(_source));

            AssertQuadrantsAndNoBorder(_readback);
        }

        [Test]
        public void PrepareEmbedding_ReusesTheSameSurfaceAcrossFrames()
        {
            _source = CreateSource(640, 480);

            var first = _preprocessor.PrepareEmbedding(_source);
            for (var call = 2; call <= 10; call++)
            {
                Assert.That(_preprocessor.PrepareEmbedding(_source), Is.SameAs(first), $"call {call} allocated a new surface");
            }
        }

        [Test]
        public void PrepareEmbedding_DoesNotAllocateManagedMemoryAfterTheFirstFrame()
        {
            _source = CreateSource(640, 480);
            _preprocessor.PrepareEmbedding(_source);

            Assert.That(
                () =>
                {
                    for (var call = 2; call <= 10; call++)
                    {
                        _preprocessor.PrepareEmbedding(_source);
                    }
                },
                Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void PrepareEmbedding_RejectsAMissingFrame()
        {
            Assert.Throws<ArgumentNullException>(() => _preprocessor.PrepareEmbedding(null));
        }

        [Test]
        public void CopyVerificationFrame_IsSquareAndBoundedToTheMaximum()
        {
            _source = CreateSource(1280, 720);

            var frame = _preprocessor.CopyVerificationFrame(_source);

            Assert.That(frame.width, Is.EqualTo(ImageGeometry.MaximumVerificationImageSize));
            Assert.That(frame.height, Is.EqualTo(ImageGeometry.MaximumVerificationImageSize));
            Assert.That(_preprocessor.VerificationSize, Is.EqualTo(ImageGeometry.MaximumVerificationImageSize));
        }

        [Test]
        public void CopyVerificationFrame_DoesNotUpscaleSmallFrames()
        {
            _source = CreateSource(240, 180);

            var frame = _preprocessor.CopyVerificationFrame(_source);

            Assert.That(frame.width, Is.EqualTo(180));
            Assert.That(frame.height, Is.EqualTo(180));
        }

        [Test]
        public void CopyVerificationFrame_IsCpuReadableAndCentreCropped()
        {
            _source = CreateSource(640, 480);

            var frame = _preprocessor.CopyVerificationFrame(_source);

            Assert.That(frame.isReadable, Is.True);
            AssertQuadrantsAndNoBorder(frame);
        }

        [Test]
        public void CopyVerificationFrame_ReusesTheSameTextureAcrossFrames()
        {
            _source = CreateSource(640, 480);

            var first = _preprocessor.CopyVerificationFrame(_source);
            for (var call = 2; call <= 10; call++)
            {
                Assert.That(_preprocessor.CopyVerificationFrame(_source), Is.SameAs(first), $"call {call} allocated a new texture");
            }
        }

        [Test]
        public void CopyVerificationFrame_NeverHandsOutTheCameraTexture()
        {
            _source = CreateSource(640, 480);

            var frame = _preprocessor.CopyVerificationFrame(_source);

            Assert.That(frame, Is.Not.SameAs(_source));
        }

        [Test]
        public void Dispose_IsIdempotentAndRejectsLaterUse()
        {
            _source = CreateSource(640, 480);
            _preprocessor.PrepareEmbedding(_source);
            _preprocessor.CopyVerificationFrame(_source);

            _preprocessor.Dispose();
            Assert.DoesNotThrow(() => _preprocessor.Dispose());

            var source = _source;
            Assert.Throws<ObjectDisposedException>(() => _preprocessor.PrepareEmbedding(source));
            Assert.Throws<ObjectDisposedException>(() => _preprocessor.CopyVerificationFrame(source));
        }

        private static void AssertQuadrantsAndNoBorder(Texture2D image)
        {
            var side = image.width;
            var low = side / 4;
            var high = side - side / 4;

            AssertGreen(image.GetPixel(low, low), "bottom-left");
            AssertBlue(image.GetPixel(high, low), "bottom-right");
            AssertWhite(image.GetPixel(low, high), "top-left");
            AssertBlack(image.GetPixel(high, high), "top-right");

            // A stretched or mis-offset crop drags the surrounding border into the result.
            var pixels = image.GetPixels();
            for (var y = 2; y < image.height - 2; y++)
            {
                for (var x = 2; x < image.width - 2; x++)
                {
                    var pixel = pixels[y * image.width + x];
                    Assert.That(
                        pixel.r > 0.5f && pixel.g < 0.35f && pixel.b < 0.35f,
                        Is.False,
                        $"border colour leaked into ({x}, {y})");
                }
            }
        }

        private static void AssertGreen(Color pixel, string corner)
        {
            Assert.That(pixel.g, Is.GreaterThan(0.5f), corner);
            Assert.That(pixel.r, Is.LessThan(0.35f), corner);
            Assert.That(pixel.b, Is.LessThan(0.35f), corner);
        }

        private static void AssertBlue(Color pixel, string corner)
        {
            Assert.That(pixel.b, Is.GreaterThan(0.5f), corner);
            Assert.That(pixel.r, Is.LessThan(0.35f), corner);
            Assert.That(pixel.g, Is.LessThan(0.35f), corner);
        }

        private static void AssertWhite(Color pixel, string corner)
        {
            Assert.That(pixel.r, Is.GreaterThan(0.5f), corner);
            Assert.That(pixel.g, Is.GreaterThan(0.5f), corner);
            Assert.That(pixel.b, Is.GreaterThan(0.5f), corner);
        }

        private static void AssertBlack(Color pixel, string corner)
        {
            Assert.That(pixel.r, Is.LessThan(0.35f), corner);
            Assert.That(pixel.g, Is.LessThan(0.35f), corner);
            Assert.That(pixel.b, Is.LessThan(0.35f), corner);
        }

        private static Texture2D CreateSource(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var side = Mathf.Min(width, height);
            var originX = (width - side) / 2;
            var originY = (height - side) / 2;
            var half = side / 2;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var localX = x - originX;
                    var localY = y - originY;
                    var color = Border;
                    if (localX >= 0 && localX < side && localY >= 0 && localY < side)
                    {
                        var right = localX >= half;
                        var top = localY >= half;
                        color = top
                            ? (right ? TopRight : TopLeft)
                            : (right ? BottomRight : BottomLeft);
                    }

                    pixels[y * width + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D ReadBack(RenderTexture surface)
        {
            var readable = new Texture2D(surface.width, surface.height, TextureFormat.RGBA32, false, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = surface;
                readable.ReadPixels(new Rect(0f, 0f, surface.width, surface.height), 0, 0, false);
                readable.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return readable;
        }

        private static void Destroy(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
