using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplateMatchTests
    {
        private const int QuerySize = 64;

        /// <summary>
        /// A flat field with a motif burned into it at (left, top). The motif is deliberately not
        /// a gradient: a linear ramp is self-similar under translation, so every sub-window of a
        /// larger ramp correlates perfectly and the object has no unique position to find. This
        /// one is defined on coordinates normalised within the block, so it keeps its shape at any
        /// size, and a partial view of it does not look like the whole.
        /// </summary>
        private static byte[] Scene(int size, int left, int top, int blockSize, byte background, byte block)
        {
            var pixels = new byte[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = background;
            }

            for (var y = top; y < top + blockSize; y++)
            {
                for (var x = left; x < left + blockSize; x++)
                {
                    var u = (x - left) / (float)blockSize;
                    var v = (y - top) / (float)blockSize;

                    var value = block;
                    if (u >= 0.15f && u < 0.40f && v >= 0.15f && v < 0.40f)
                    {
                        value = (byte)Math.Max(0, block - 45);      // a notch near one corner
                    }
                    else if (u >= 0.55f && u < 0.90f && v >= 0.50f && v < 0.75f)
                    {
                        value = (byte)Math.Min(255, block + 90);    // a bar near the opposite one
                    }

                    pixels[(y * size) + x] = value;
                }
            }

            return pixels;
        }

        private static readonly float[] UnitScale = { 1f };

        [Test]
        public void TryLocate_FindsATemplateAtTheCentreOfTheQuery()
        {
            var template = Scene(16, 0, 0, 16, 40, 120);
            var query = Scene(QuerySize, 24, 24, 16, 40, 120);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x, out var y, out var score);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(y, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(score, Is.GreaterThan(0.9f));
        }

        [Test]
        public void TryLocate_ReportsTheOffsetWhenTheObjectHasMoved()
        {
            var template = Scene(16, 0, 0, 16, 40, 120);
            // Block at x=8..24, y=40..56; its centre is (16, 48) in a 64 wide view.
            var query = Scene(QuerySize, 8, 40, 16, 40, 120);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x, out var y, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(16f / QuerySize).Within(0.02f));
            // Bottom-left origin, so a block low in the image reports a low y.
            Assert.That(y, Is.EqualTo(1f - (48f / QuerySize)).Within(0.02f));
        }

        [Test]
        public void TryLocate_IsUnaffectedByGainAndOffset()
        {
            // The property the whole lighting result rests on: correlation is normalised, so the
            // same scene brightened and lifted must land in the same place with the same score.
            var template = Scene(16, 0, 0, 16, 40, 120);
            var query = Scene(QuerySize, 24, 24, 16, 40, 120);
            var brightened = new byte[query.Length];
            for (var i = 0; i < query.Length; i++)
            {
                brightened[i] = (byte)Math.Min(255, (int)(query[i] * 0.6f) + 90);
            }

            TemplateMatch.TryLocate(template, 16, query, QuerySize, UnitScale, 0.5f,
                out var x0, out var y0, out var s0);
            var found = TemplateMatch.TryLocate(template, 16, brightened, QuerySize, UnitScale, 0.5f,
                out var x1, out var y1, out var s1);

            Assert.That(found, Is.True);
            Assert.That(x1, Is.EqualTo(x0).Within(1e-4f));
            Assert.That(y1, Is.EqualTo(y0).Within(1e-4f));
            Assert.That(s1, Is.EqualTo(s0).Within(0.05f));
        }

        [Test]
        public void TryLocate_FindsAnObjectThatMovedCloser()
        {
            var template = Scene(16, 0, 0, 16, 40, 120);
            // The same block drawn twice as large: only the 2.0 scale can match it.
            var query = Scene(QuerySize, 16, 16, 32, 40, 120);

            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, new[] { 1f, 2f }, 0.5f,
                out var x, out var y, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(y, Is.EqualTo(0.5f).Within(0.05f));
        }

        [Test]
        public void TryLocate_RefusesAQueryThatDoesNotContainTheTemplate()
        {
            var template = Scene(16, 0, 0, 16, 40, 120);
            var noise = new byte[QuerySize * QuerySize];
            var rng = new Random(11);
            rng.NextBytes(noise);

            var found = TemplateMatch.TryLocate(
                template, 16, noise, QuerySize, UnitScale, 0.9f,
                out var x, out var y, out _);

            Assert.That(found, Is.False);
            Assert.That(x, Is.EqualTo(0f));
            Assert.That(y, Is.EqualTo(0f));
        }

        [Test]
        public void TryLocate_SkipsAScaleThatWouldExceedTheQuery()
        {
            var template = Scene(16, 0, 0, 16, 40, 120);
            var query = Scene(QuerySize, 24, 24, 16, 40, 120);

            // 8.0 would need a 128px template inside a 64px query; 1.0 must still be tried.
            var found = TemplateMatch.TryLocate(
                template, 16, query, QuerySize, new[] { 8f, 1f }, 0.5f,
                out var x, out _, out _);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void TryLocate_RejectsPixelCountsThatDoNotMatchTheDeclaredSize()
        {
            Assert.Throws<ArgumentException>(() => TemplateMatch.TryLocate(
                new byte[10], 16, new byte[QuerySize * QuerySize], QuerySize,
                UnitScale, 0.5f, out _, out _, out _));
        }
    }
}
