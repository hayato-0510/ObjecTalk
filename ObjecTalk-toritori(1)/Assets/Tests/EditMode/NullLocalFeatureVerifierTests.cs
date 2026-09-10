using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    /// <summary>
    /// The fallback used when the OpenCV native library is not in the build (an ARM64 player with
    /// no arm64 OpenCvSharpExtern): local-feature verification is skipped, so a candidate that
    /// passed neural screening is treated as confirmed, and captures still persist and reload.
    /// </summary>
    [TestFixture]
    public sealed class NullLocalFeatureVerifierTests
    {
        private NullLocalFeatureVerifier _verifier;
        private Texture2D _image;

        [SetUp]
        public void SetUp()
        {
            _verifier = new NullLocalFeatureVerifier();
            _verifier.Initialize();
            _image = new Texture2D(4, 4);
        }

        [TearDown]
        public void TearDown()
        {
            _verifier.Dispose();
            Object.DestroyImmediate(_image);
        }

        [Test]
        public void VerifyAlwaysPassesSoNeuralScreeningDecidesAlone()
        {
            using var reference = _verifier.Extract(_image);

            var verification = _verifier.Verify(
                _image, reference, new LocalVerificationThresholds(999, 0.99f, gatesRecognition: true));

            Assert.That(verification.IsVerified, Is.True);
            Assert.That(verification.HasLocation, Is.False, "Without keypoints there is nothing to locate.");
        }

        [Test]
        public void ExtractReturnsASampleThatReportsFeaturesAndCanBePersisted()
        {
            using var sample = _verifier.Extract(_image);

            Assert.That(sample, Is.Not.Null);
            Assert.That(sample.HasFeatures, Is.True, "A featureless sample would loop the user on 'retry'.");
            Assert.That(sample.Serialize().HasPayload, Is.True, "The persistence layer rejects an empty blob.");
        }

        [Test]
        public void ARestoredSampleIsUsable()
        {
            using var original = _verifier.Extract(_image);
            using var restored = _verifier.Restore(original.Serialize());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.HasFeatures, Is.True);
        }
    }
}
