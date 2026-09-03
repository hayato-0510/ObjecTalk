using System.Collections.Generic;
using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using ObjecTalk.Infrastructure.Vision;
using UnityEngine;

namespace ObjecTalk.Tests.PlayMode
{
    /// <summary>
    /// Lifecycle behaviour of the recognition orchestrator: what pause abandons, what it releases,
    /// and what resume restores. The graphics-dependent cases drive the real
    /// <see cref="CameraFramePreprocessor"/>, which is why they live in Play Mode.
    /// </summary>
    [TestFixture]
    public sealed class ObjectRecognitionLifecycleTests
    {
        private const string ObjectA = "object-a";

        private static readonly RecognitionDataVersion Version =
            new RecognitionDataVersion(1, "model-v1", "prep-v1");

        private FakeEmbeddingExtractor _extractor;
        private FakeLocalFeatureVerifier _verifier;
        private FakeRecognitionSampleStore _store;
        private ObjectRecognitionService _service;
        private Texture2D _frame;
        private readonly List<RecognitionStateChange> _changes = new List<RecognitionStateChange>();

        [SetUp]
        public void SetUp()
        {
            _extractor = new FakeEmbeddingExtractor();
            _verifier = new FakeLocalFeatureVerifier();
            _store = new FakeRecognitionSampleStore();
            _frame = new Texture2D(64, 48, TextureFormat.RGBA32, false, false);
            _changes.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
            if (_frame != null)
            {
                Object.DestroyImmediate(_frame);
                _frame = null;
            }
        }

        [Test]
        public void PauseCancelsTheActiveGeneration()
        {
            var preprocessor = BuildWithFakePreprocessor();
            SeedOneObject();
            _extractor.ScriptSteps(EmbeddingStepResult.Pending);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            var runningGeneration = _extractor.Generation;

            _service.Pause();

            Assert.That(_extractor.LastCancelledGeneration, Is.EqualTo(runningGeneration));
            Assert.That(_service.IsBusy, Is.False);
            Assert.That(preprocessor.ReleaseBufferCount, Is.EqualTo(1));
        }

        [Test]
        public void PauseStopsAcceptingFrames()
        {
            BuildWithFakePreprocessor();
            SeedOneObject();
            _service.Pause();

            Assert.That(_service.StartRecognition(_frame, 5d), Is.False);
            Assert.That(_service.Tick(5d).Status, Is.EqualTo(RecognitionTickStatus.Idle));
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        [Test]
        public void PauseRefusesRegistrationCaptures()
        {
            BuildWithFakePreprocessor();
            _service.BeginRegistration(ObjectA);
            _service.Pause();

            Assert.That(
                _service.CaptureRegistrationView(_frame),
                Is.EqualTo(RegistrationCaptureStatus.Unavailable));
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        [Test]
        public void PausePublishesTheLossOfRecognition()
        {
            BuildWithFakePreprocessor(requiredConsecutiveHits: 1);
            SeedOneObject();
            _extractor.ScriptResult(1f, 0f);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            Assert.That(_service.Tick(0.05d).Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(_service.RecognizedObjectId, Is.EqualTo(ObjectA));

            _service.Pause();

            Assert.That(_service.RecognizedObjectId, Is.Null);
            Assert.That(_changes.Count, Is.EqualTo(2));
            Assert.That(_changes[1].IsRecognized, Is.False);
        }

        [Test]
        public void ResumeCreatesACleanGeneration()
        {
            BuildWithFakePreprocessor();
            SeedOneObject();
            _extractor.ScriptSteps(EmbeddingStepResult.Pending);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            var cancelledGeneration = _extractor.Generation;
            _service.Pause();
            _service.Resume();

            Assert.That(_service.IsPaused, Is.False);
            _extractor.ScriptSteps(EmbeddingStepResult.Pending);
            Assert.That(_service.StartRecognition(_frame, 10d), Is.True);
            Assert.That(
                _extractor.Generation,
                Is.GreaterThan(cancelledGeneration),
                "A resumed run must not reuse a generation a stale result could claim.");
        }

        [Test]
        public void ResumeAcceptsFramesAgain()
        {
            BuildWithFakePreprocessor(requiredConsecutiveHits: 1);
            SeedOneObject();
            _extractor.ScriptResult(1f, 0f);
            _service.Pause();
            _service.Resume();

            Assert.That(_service.StartRecognition(_frame, 10d), Is.True);
            Assert.That(_service.Tick(10.05d).Status, Is.EqualTo(RecognitionTickStatus.Recognized));
        }

        [Test]
        public void DisposeReleasesEveryCollaborator()
        {
            var preprocessor = BuildWithFakePreprocessor();
            SeedOneObject();

            _service.Dispose();
            var disposed = _service;
            _service = null;

            Assert.That(_extractor.DisposeCount, Is.EqualTo(1));
            Assert.That(preprocessor.DisposeCount, Is.EqualTo(1));
            Assert.That(_verifier.DisposeCount, Is.EqualTo(1));
            Assert.That(() => disposed.StartRecognition(_frame, 0d), Throws.TypeOf<System.ObjectDisposedException>());
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            BuildWithFakePreprocessor();

            _service.Dispose();
            Assert.That(() => _service.Dispose(), Throws.Nothing);
            Assert.That(_extractor.DisposeCount, Is.EqualTo(1));
            _service = null;
        }

        [Test]
        public void PauseReleasesTheRealPreprocessorBuffers()
        {
            var preprocessor = new CameraFramePreprocessor();
            BuildWith(preprocessor, RecognitionSettings.DefaultRequiredConsecutiveHits);
            SeedOneObject();
            _extractor.ScriptSteps(EmbeddingStepResult.Pending);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            preprocessor.CopyVerificationFrame(_frame);
            Assert.That(preprocessor.VerificationSize, Is.GreaterThan(0));

            _service.Pause();

            Assert.That(preprocessor.VerificationSize, Is.Zero, "Pausing must hand the graphics buffers back.");

            _service.Resume();
            var reacquired = preprocessor.PrepareEmbedding(_frame);

            Assert.That(reacquired, Is.Not.Null);
            Assert.That(reacquired.width, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
        }

        [Test]
        public void TheRealPreprocessorFeedsTheExtractorTheEmbeddingSquare()
        {
            var preprocessor = new CameraFramePreprocessor();
            BuildWith(preprocessor, requiredConsecutiveHits: 1);
            SeedOneObject();
            _extractor.ScriptResult(1f, 0f);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);

            Assert.That(_extractor.LastInput, Is.Not.Null);
            Assert.That(_extractor.LastInput.width, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
            Assert.That(_extractor.LastInput.height, Is.EqualTo(ImageGeometry.EmbeddingImageSize));

            var result = _service.Tick(0.05d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(_verifier.VerifyCallCount, Is.EqualTo(1));
        }

        private FakeFramePreprocessor BuildWithFakePreprocessor(
            int requiredConsecutiveHits = RecognitionSettings.DefaultRequiredConsecutiveHits)
        {
            var preprocessor = new FakeFramePreprocessor();
            BuildWith(preprocessor, requiredConsecutiveHits);
            return preprocessor;
        }

        private void BuildWith(IFramePreprocessor preprocessor, int requiredConsecutiveHits)
        {
            var settings = new RecognitionSettings(
                0.7f,
                0.05f,
                requiredConsecutiveHits,
                RecognitionSettings.DefaultLossGracePeriodSeconds,
                RecognitionSettings.MinimumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.MaximumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.DefaultMinimumLocalMatchCount);

            _service = new ObjectRecognitionService(
                _extractor,
                preprocessor,
                _verifier,
                _store,
                settings,
                Version);
            _service.RecognitionStateChanged += change => _changes.Add(change);
        }

        private void SeedOneObject()
        {
            _store.Seed(new RecognitionSampleRecord(
                ObjectA,
                0,
                Version,
                new[] { 1f, 0f },
                new byte[] { 1, 2, 3, 4 },
                1,
                4,
                0,
                new byte[] { 5, 6, 7, 8 },
                64,
                64));
            _service.LoadSamples("user-1");
        }
    }
}
