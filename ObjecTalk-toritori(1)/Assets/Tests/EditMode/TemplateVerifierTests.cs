using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.Vision;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    public class TemplateVerifierTests
    {
        private const int FrameSize = 128;

        private TemplateVerifier _verifier;

        private static readonly LocalVerificationThresholds Lenient =
            new LocalVerificationThresholds(10, 0.3f, gatesRecognition: false);

        [SetUp]
        public void SetUp()
        {
            _verifier = new TemplateVerifier();
            _verifier.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _verifier.Dispose();
        }

        /// <summary>
        /// A frame with a motif centred on (<paramref name="centreU"/>, <paramref name="centreV"/>),
        /// given in the same 0..1 bottom-left-origin coordinates a located object is reported in.
        /// The motif is asymmetric and defined on block-relative coordinates, so it survives
        /// rescaling and, unlike a gradient, does not look like itself when partly seen.
        /// </summary>
        private static Texture2D Frame(float centreU, float centreV, float sizeFraction)
        {
            var texture = new Texture2D(FrameSize, FrameSize, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[FrameSize * FrameSize];

            var side = Mathf.RoundToInt(FrameSize * sizeFraction);
            var left = Mathf.RoundToInt((centreU * FrameSize) - (side / 2f));
            // Color32 rows run bottom-up in Unity, and so does v, so no flip is needed here.
            var bottom = Mathf.RoundToInt((centreV * FrameSize) - (side / 2f));

            for (var y = 0; y < FrameSize; y++)
            {
                for (var x = 0; x < FrameSize; x++)
                {
                    byte value = 45;
                    if (x >= left && x < left + side && y >= bottom && y < bottom + side)
                    {
                        var u = (x - left) / (float)side;
                        var v = (y - bottom) / (float)side;
                        value = 130;
                        if (u >= 0.15f && u < 0.40f && v >= 0.55f && v < 0.85f)
                        {
                            value = 40;
                        }
                        else if (u >= 0.55f && u < 0.90f && v >= 0.20f && v < 0.45f)
                        {
                            value = 225;
                        }
                    }

                    pixels[(y * FrameSize) + x] = new Color32(value, value, value, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>The object has left the view entirely: a flat field with nothing to match.</summary>
        private static Texture2D EmptyFrame()
        {
            var texture = new Texture2D(FrameSize, FrameSize, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[FrameSize * FrameSize];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(45, 45, 45, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        [Test]
        public void ExtractProducesAReferenceThatReportsFeatures()
        {
            var frame = Frame(0.5f, 0.5f, 0.375f);
            try
            {
                using var sample = _verifier.Extract(frame);

                Assert.That(sample.HasFeatures, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(frame);
            }
        }

        [Test]
        public void VerifyLocatesACentredObjectAtTheCentre()
        {
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = Frame(0.5f, 0.5f, 0.375f);
            try
            {
                using var reference = _verifier.Extract(registration);

                var result = _verifier.Verify(live, reference, Lenient);

                Assert.That(result.IsVerified, Is.True);
                Assert.That(result.HasLocation, Is.True);
                Assert.That(result.RelativeX, Is.EqualTo(0.5f).Within(0.08f));
                Assert.That(result.RelativeY, Is.EqualTo(0.5f).Within(0.08f));
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void VerifyFollowsAnObjectThatMovedLeft()
        {
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = Frame(0.35f, 0.5f, 0.375f);
            try
            {
                using var reference = _verifier.Extract(registration);

                var result = _verifier.Verify(live, reference, Lenient);

                Assert.That(result.HasLocation, Is.True);
                Assert.That(result.RelativeX, Is.LessThan(0.45f));
                Assert.That(result.RelativeY, Is.EqualTo(0.5f).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void VerifyFollowsAnObjectThatMovedDownNotUp()
        {
            // Guards the vertical axis end to end: Unity hands over bottom-up rows, the template
            // is stored top-down, and the reported position is bottom-up again. Get any one of
            // those wrong and the face chases the object in the opposite direction.
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = Frame(0.5f, 0.35f, 0.375f);
            try
            {
                using var reference = _verifier.Extract(registration);

                var result = _verifier.Verify(live, reference, Lenient);

                Assert.That(result.HasLocation, Is.True);
                Assert.That(result.RelativeY, Is.LessThan(0.45f));
                Assert.That(result.RelativeX, Is.EqualTo(0.5f).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void VerifyStillConfirmsWhenItCannotLocateAndTheGateIsOff()
        {
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = EmptyFrame();
            try
            {
                using var reference = _verifier.Extract(registration);

                var result = _verifier.Verify(live, reference, Lenient);

                Assert.That(result.IsVerified, Is.True, "the gate is off, so identity is untouched");
                Assert.That(result.HasLocation, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void VerifyRejectsWhenItCannotLocateAndTheGateIsOn()
        {
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = EmptyFrame();
            try
            {
                using var reference = _verifier.Extract(registration);
                var gating = new LocalVerificationThresholds(10, 0.3f, gatesRecognition: true);

                var result = _verifier.Verify(live, reference, gating);

                Assert.That(result.IsVerified, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void SerializeAndRestoreRoundTripATemplate()
        {
            var registration = Frame(0.5f, 0.5f, 0.375f);
            var live = Frame(0.35f, 0.5f, 0.375f);
            try
            {
                using var original = _verifier.Extract(registration);

                using var restored = _verifier.Restore(original.Serialize());

                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.HasFeatures, Is.True);
                var fromOriginal = _verifier.Verify(live, original, Lenient);
                var fromRestored = _verifier.Verify(live, restored, Lenient);
                Assert.That(fromRestored.RelativeX, Is.EqualTo(fromOriginal.RelativeX).Within(1e-4f));
                Assert.That(fromRestored.RelativeY, Is.EqualTo(fromOriginal.RelativeY).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(registration);
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void RestoreRefusesABlobWrittenByAnotherBackend()
        {
            // AKAZE descriptors are not a template. Reading them as one would produce a confident
            // position from noise, which is worse than reporting none.
            var foreign = new LocalFeatureBlob(new byte[] { 0 }, new byte[] { 0 }, 1, 1, 0, 1, 1);

            var restored = _verifier.Restore(foreign);

            Assert.That(restored == null || !restored.HasFeatures, Is.True);
            restored?.Dispose();
        }
    }
}
