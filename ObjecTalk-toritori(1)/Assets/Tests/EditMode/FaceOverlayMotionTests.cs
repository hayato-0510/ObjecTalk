using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class FaceOverlayMotionTests
    {
        [Test]
        public void Rms_OfSilence_IsZero()
        {
            Assert.That(FaceOverlayMotion.Rms(new float[64], 64), Is.EqualTo(0f));
            Assert.That(FaceOverlayMotion.Rms(null, 64), Is.EqualTo(0f));
            Assert.That(FaceOverlayMotion.Rms(new float[8], 0), Is.EqualTo(0f));
        }

        [Test]
        public void Rms_OfAFullScaleSquareWave_IsOne()
        {
            var samples = new float[128];
            for (int i = 0; i < samples.Length; i++) samples[i] = i % 2 == 0 ? 1f : -1f;
            Assert.That(FaceOverlayMotion.Rms(samples, samples.Length), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Rms_NeverReadsPastTheRequestedCount()
        {
            var samples = new float[] { 1f, 1f, 9f, 9f };
            Assert.That(FaceOverlayMotion.Rms(samples, 2), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void MouthOpenness_ClosesBelowTheNoiseFloor()
        {
            Assert.That(FaceOverlayMotion.MouthOpenness(0.01f, 10f, 0.02f), Is.EqualTo(0f));
            Assert.That(FaceOverlayMotion.MouthOpenness(0.5f, 0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void MouthOpenness_ScalesWithGainAndClampsToOne()
        {
            Assert.That(FaceOverlayMotion.MouthOpenness(0.12f, 10f, 0.02f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(FaceOverlayMotion.MouthOpenness(0.07f, 10f, 0.02f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void MouthScaleY_LerpsBetweenClosedAndOpen()
        {
            Assert.That(FaceOverlayMotion.MouthScaleY(0f, 0.35f, 1.15f), Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(FaceOverlayMotion.MouthScaleY(1f, 0.35f, 1.15f), Is.EqualTo(1.15f).Within(1e-5f));
            Assert.That(FaceOverlayMotion.MouthScaleY(0.5f, 0.35f, 1.15f), Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(FaceOverlayMotion.MouthScaleY(2f, 0.35f, 1.15f), Is.EqualTo(1.15f).Within(1e-5f));
        }
    }
}
