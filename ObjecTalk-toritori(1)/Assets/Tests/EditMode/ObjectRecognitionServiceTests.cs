using System;
using System.Collections.Generic;
using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    [TestFixture]
    public sealed class ObjectRecognitionServiceTests
    {
        private const string ObjectA = "object-a";
        private const string ObjectB = "object-b";

        private static readonly RecognitionDataVersion Version =
            new RecognitionDataVersion(1, "model-v1", "prep-v1");

        private FakeEmbeddingExtractor _extractor;
        private FakeFramePreprocessor _preprocessor;
        private FakeLocalFeatureVerifier _verifier;
        private FakeRecognitionSampleStore _store;
        private ObjectRecognitionService _service;
        private Texture2D _frame;
        private readonly List<RecognitionStateChange> _changes = new List<RecognitionStateChange>();

        [SetUp]
        public void SetUp()
        {
            _extractor = new FakeEmbeddingExtractor();
            _preprocessor = new FakeFramePreprocessor();
            _verifier = new FakeLocalFeatureVerifier();
            _store = new FakeRecognitionSampleStore();
            _frame = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
            _changes.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
            if (_frame != null)
            {
                UnityEngine.Object.DestroyImmediate(_frame);
                _frame = null;
            }
        }

        // --- settings propagation -------------------------------------------------------------

        [Test]
        public void SimilarityThresholdComesFromSettings()
        {
            Build(Settings(similarityThreshold: 0.99f, requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(Normalize(0.9f, 0.436f));

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.NotRecognized));
            Assert.That(_verifier.VerifyCallCount, Is.Zero, "A candidate below the configured threshold must not reach verification.");
        }

        [Test]
        public void LoweredSimilarityThresholdAdmitsTheSameFrame()
        {
            Build(Settings(similarityThreshold: 0.8f, requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(Normalize(0.9f, 0.436f));

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.ObjectId, Is.EqualTo(ObjectA));
        }

        [Test]
        public void ThresholdsCarryBothTheMatchCountAndTheTemplateScore()
        {
            var settings = Settings(minimumLocalMatchCount: 17, minimumTemplateScore: 0.42f);

            var thresholds = LocalVerificationThresholds.From(settings);

            Assert.That(thresholds.MinimumMatches, Is.EqualTo(17));
            Assert.That(thresholds.MinimumScore, Is.EqualTo(0.42f).Within(1e-6f));
        }

        [Test]
        public void RecognitionIsNotGatedByTheTemplateScoreUnlessSettingsSaySo()
        {
            // The shipped default must be unable to reject anything the embedding accepted.
            var thresholds = LocalVerificationThresholds.From(Settings());

            Assert.That(thresholds.GatesRecognition, Is.False);
        }

        [Test]
        public void TheVerifierIsGivenTheThresholdsFromSettings()
        {
            Build(Settings(minimumLocalMatchCount: 17, requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_verifier.LastThresholds.MinimumMatches, Is.EqualTo(17));
        }

        [Test]
        public void AmbiguityMarginComesFromSettings()
        {
            Build(Settings(ambiguityMargin: 0.5f, requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)), (ObjectB, Normalize(0.99f, 0.141f)));
            _extractor.ScriptResult(1f, 0f);

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.NotRecognized));
            Assert.That(_verifier.VerifyCallCount, Is.Zero, "An ambiguous best candidate must not reach verification.");
        }

        // --- object location ------------------------------------------------------------------

        [Test]
        public void RecognizedTickCarriesTheLocationVerificationFound()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyLocation = (0.7f, 0.3f);

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.HasLocation, Is.True);
            Assert.That(result.RelativeX, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(result.RelativeY, Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void RecognizedTickReportsNoLocationWhenTheMatchWasTooSparseToPlace()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyLocation = null;

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.HasLocation, Is.False);
        }

        [Test]
        public void UnrecognizedTickReportsNoLocation()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyResult = false;
            _verifier.VerifyLocation = (0.7f, 0.3f);

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.NotRecognized));
            Assert.That(result.HasLocation, Is.False);
        }

        // --- loading resilience ---------------------------------------------------------------

        [Test]
        public void AnObjectWhoseLocalFeaturesCannotBeRestoredIsStillRecognized()
        {
            // A backend that cannot read the persisted blob must cost the object its position,
            // never its embedding - otherwise switching backends unregisters everything.
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.ObjectId, Is.EqualTo(ObjectA));
            Assert.That(result.HasLocation, Is.False);
        }

        [Test]
        public void AnObjectWithNoReferencesNeverReachesTheVerifier()
        {
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifyCallCount, Is.Zero);
        }

        // --- seeding --------------------------------------------------------------------------

        [Test]
        public void SeededReferencesGiveAnObjectAPositionItCouldNotRestore()
        {
            _verifier.RestoreFails = true;
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyLocation = (0.62f, 0.28f);

            _service.SeedReferences(ObjectA, new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") });
            var result = RunOneCycle(now: 0d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.HasLocation, Is.True);
            Assert.That(result.RelativeX, Is.EqualTo(0.62f).Within(1e-6f));
        }

        [Test]
        public void SeedingLeavesARestoredReferenceAlone()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));

            _service.SeedReferences(ObjectA, new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") });
            _extractor.ScriptResult(1f, 0f);
            RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifiedAgainst.Count, Is.EqualTo(1),
                "the persisted reference is authoritative; seeding must not add a second one");
        }

        [Test]
        public void SeedingAnUnknownObjectIsIgnored()
        {
            Build(Settings());
            SeedObjects((ObjectA, Unit(1f, 0f)));

            Assert.DoesNotThrow(() => _service.SeedReferences(
                "no-such-object", new ILocalFeatureSample[] { new FakeLocalFeatureSample("seeded") }));
        }

        // --- verification gating --------------------------------------------------------------

        [Test]
        public void OnlyTheBestCandidateReachesVerification()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)), (ObjectB, Unit(0f, 1f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifyCallCount, Is.EqualTo(1));
            Assert.That(_verifier.VerifiedAgainst.Count, Is.EqualTo(1));
        }

        [Test]
        public void VerificationFailureKeepsTheCandidateOut()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);
            _verifier.VerifyResult = false;

            var result = RunOneCycle(now: 0d);

            Assert.That(_verifier.VerifyCallCount, Is.EqualTo(1));
            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.NotRecognized));
            Assert.That(_service.RecognizedObjectId, Is.Null);
        }

        // --- non-overlap ----------------------------------------------------------------------

        [Test]
        public void InferenceNeverOverlaps()
        {
            Build(Settings());
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptSteps(EmbeddingStepResult.Pending, EmbeddingStepResult.Pending);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            _service.Tick(0.01d);

            Assert.That(_service.StartRecognition(_frame, 10d), Is.False, "A second run must not start while one is in flight.");
            Assert.That(_extractor.BeginCount, Is.EqualTo(1));
        }

        [Test]
        public void FramesAreSkippedUntilTheNextScheduledCycle()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.False, "The adaptive cadence must gate the next start.");
            Assert.That(_service.StartRecognition(_frame, 5d), Is.True);
        }

        [Test]
        public void RecognitionRefusesToStartWithoutRegisteredSamples()
        {
            Build(Settings());

            Assert.That(_service.StartRecognition(_frame, 0d), Is.False);
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        [Test]
        public void RecognitionRefusesToStartWhileTheExtractorIsDisabled()
        {
            Build(Settings());
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.IsDisabled = true;

            Assert.That(_service.StartRecognition(_frame, 0d), Is.False);
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        // --- stabilized state events ----------------------------------------------------------

        [Test]
        public void StateEventsPublishOnlyStabilizedChanges()
        {
            Build(Settings(requiredConsecutiveHits: 2));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptResult(1f, 0f);

            RunOneCycle(now: 0d);
            Assert.That(_changes, Is.Empty, "One hit is not yet stable evidence.");

            RunOneCycle(now: 0.3d);
            Assert.That(_changes.Count, Is.EqualTo(1));
            Assert.That(_changes[0].ObjectId, Is.EqualTo(ObjectA));
            Assert.That(_changes[0].IsRecognized, Is.True);

            RunOneCycle(now: 0.6d);
            Assert.That(_changes.Count, Is.EqualTo(1), "An unchanged stable state must not republish.");
        }

        [Test]
        public void FailedInferenceIsReportedAndReleasesTheRun()
        {
            Build(Settings(requiredConsecutiveHits: 1));
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _extractor.ScriptSteps(EmbeddingStepResult.Failed);

            Assert.That(_service.StartRecognition(_frame, 0d), Is.True);
            var result = _service.Tick(0.05d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Failed));
            Assert.That(_service.IsBusy, Is.False);
            Assert.That(_service.StartRecognition(_frame, 5d), Is.True);
        }

        // --- loading --------------------------------------------------------------------------

        [Test]
        public void LoadingInvalidatesObsoleteRowsFirst()
        {
            Build(Settings());
            _store.Seed(Record(ObjectA, 0, Unit(1f, 0f)));

            _service.LoadSamples("user-1");

            Assert.That(_store.InvalidateCount, Is.EqualTo(1));
            Assert.That(_service.LoadedSampleCount, Is.EqualTo(1));
            Assert.That(_service.RegisteredObjectCount, Is.EqualTo(1));
        }

        [Test]
        public void ASampleWhoseLocalFeaturesCannotBeRestoredKeepsItsEmbedding()
        {
            // This used to drop the whole record. A backend that cannot read the persisted blob
            // - the template backend reading AKAZE descriptors, say - would then unregister every
            // object it inherited. Losing the position is acceptable; losing the identity is not.
            Build(Settings());
            _store.Seed(Record(ObjectA, 0, Unit(1f, 0f)));
            _verifier.RestoreFails = true;

            _service.LoadSamples("user-1");

            Assert.That(_service.LoadedSampleCount, Is.EqualTo(1), "the embedding survives");
            Assert.That(_service.RegisteredObjectCount, Is.Zero, "but it has no reference to match against");
        }

        // --- registration ---------------------------------------------------------------------

        [Test]
        public void RegistrationCannotCommitWithoutCaptures()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);

            Assert.That(_service.CompleteRegistration(), Is.False);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void RegistrationCannotCommitWithOneCapture()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            CaptureOneView(1f, 0f);

            Assert.That(_service.CapturedViewCount, Is.EqualTo(1));
            Assert.That(_service.CompleteRegistration(), Is.False);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void RegistrationCannotCommitWithTwoCaptures()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            CaptureOneView(1f, 0f);
            CaptureOneView(0f, 1f);

            Assert.That(_service.CapturedViewCount, Is.EqualTo(2));
            Assert.That(_service.CompleteRegistration(), Is.False);
            Assert.That(_store.SaveCount, Is.Zero);
        }

        [Test]
        public void AFeaturelessCaptureAsksForARetryWithoutRunningInference()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            _verifier.ExtractHasFeatures = false;

            var status = _service.CaptureRegistrationView(_frame);

            Assert.That(status, Is.EqualTo(RegistrationCaptureStatus.Featureless));
            Assert.That(_service.CapturedViewCount, Is.Zero);
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        [Test]
        public void AFailedCaptureDoesNotCountTowardTheThreeViews()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            _extractor.ScriptSteps(EmbeddingStepResult.Failed);

            Assert.That(_service.CaptureRegistrationView(_frame), Is.EqualTo(RegistrationCaptureStatus.Started));
            var result = _service.Tick(0.05d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Failed));
            Assert.That(_service.CapturedViewCount, Is.Zero);
        }

        [Test]
        public void ASecondCaptureIsRefusedWhileTheFirstIsStillRunning()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            _extractor.ScriptSteps(EmbeddingStepResult.Pending);

            Assert.That(_service.CaptureRegistrationView(_frame), Is.EqualTo(RegistrationCaptureStatus.Started));
            _service.Tick(0.01d);

            Assert.That(_service.CaptureRegistrationView(_frame), Is.EqualTo(RegistrationCaptureStatus.Unavailable));
            Assert.That(_extractor.BeginCount, Is.EqualTo(1));
        }

        [Test]
        public void AFourthCaptureIsRefused()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            CaptureOneView(1f, 0f);
            CaptureOneView(0f, 1f);
            CaptureOneView(Normalize(1f, 1f));

            Assert.That(_service.CaptureRegistrationView(_frame), Is.EqualTo(RegistrationCaptureStatus.NotAccepting));
            Assert.That(_service.CapturedViewCount, Is.EqualTo(3));
        }

        [Test]
        public void ThreeValidCapturesCommitAtomically()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            CaptureOneView(1f, 0f);
            CaptureOneView(0f, 1f);
            CaptureOneView(Normalize(1f, 1f));

            Assert.That(_service.CompleteRegistration(), Is.True);
            Assert.That(_store.SaveCount, Is.EqualTo(1), "All three samples must be written in one call.");
            Assert.That(_store.LastSavedObjectId, Is.EqualTo(ObjectA));
            Assert.That(_store.LastSavedSamples.Count, Is.EqualTo(3));
            Assert.That(
                new[]
                {
                    _store.LastSavedSamples[0].SampleIndex,
                    _store.LastSavedSamples[1].SampleIndex,
                    _store.LastSavedSamples[2].SampleIndex
                },
                Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(_store.LastSavedSamples[0].Version, Is.EqualTo(Version));
            Assert.That(_service.IsRegistering, Is.False);
            Assert.That(_service.RegisteredObjectCount, Is.EqualTo(1));
        }

        [Test]
        public void AFailedWriteLeavesTheServiceMatchingWhatIsPersisted()
        {
            Build(Settings());
            _service.BeginRegistration(ObjectA);
            CaptureOneView(1f, 0f);
            CaptureOneView(0f, 1f);
            CaptureOneView(Normalize(1f, 1f));
            _store.FailSave = true;

            Assert.That(() => _service.CompleteRegistration(), Throws.InvalidOperationException);
            Assert.That(_service.RegisteredObjectCount, Is.Zero);
            Assert.That(_service.IsRegistering, Is.True, "A rejected write must leave the registration open.");
        }

        [Test]
        public void RecognitionDoesNotRunDuringRegistration()
        {
            Build(Settings());
            SeedObjects((ObjectA, Unit(1f, 0f)));
            _service.BeginRegistration(ObjectB);

            Assert.That(_service.StartRecognition(_frame, 5d), Is.False);
            Assert.That(_extractor.BeginCount, Is.Zero);
        }

        // --- helpers --------------------------------------------------------------------------

        private static RecognitionSettings Settings(
            float similarityThreshold = 0.7f,
            float ambiguityMargin = 0.05f,
            int requiredConsecutiveHits = RecognitionSettings.DefaultRequiredConsecutiveHits,
            int minimumLocalMatchCount = RecognitionSettings.DefaultMinimumLocalMatchCount,
            float minimumTemplateScore = RecognitionSettings.DefaultMinimumTemplateScore,
            bool templateGatesRecognition = false)
        {
            return new RecognitionSettings(
                similarityThreshold,
                ambiguityMargin,
                requiredConsecutiveHits,
                RecognitionSettings.DefaultLossGracePeriodSeconds,
                RecognitionSettings.MinimumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.MaximumSupportedRecognitionIntervalSeconds,
                minimumLocalMatchCount,
                minimumTemplateScore,
                templateGatesRecognition);
        }

        private void Build(RecognitionSettings settings)
        {
            _service = new ObjectRecognitionService(
                _extractor,
                _preprocessor,
                _verifier,
                _store,
                settings,
                Version);
            _service.RecognitionStateChanged += change => _changes.Add(change);
        }

        private void SeedObjects(params (string ObjectId, float[] Embedding)[] objects)
        {
            var records = new List<RecognitionSampleRecord>();
            foreach (var entry in objects)
            {
                records.Add(Record(entry.ObjectId, 0, entry.Embedding));
            }

            _store.Seed(records.ToArray());
            _service.LoadSamples("user-1");
        }

        private static RecognitionSampleRecord Record(string objectId, int index, float[] embedding)
        {
            return new RecognitionSampleRecord(
                objectId,
                index,
                Version,
                embedding,
                new byte[] { 1, 2, 3, 4 },
                1,
                4,
                0,
                new byte[] { 5, 6, 7, 8 },
                64,
                64);
        }

        private RecognitionTickResult RunOneCycle(double now)
        {
            Assert.That(_service.StartRecognition(_frame, now), Is.True, "The cycle under test could not start.");
            return _service.Tick(now + 0.05d);
        }

        private void CaptureOneView(params float[] embedding)
        {
            _extractor.ScriptSteps(EmbeddingStepResult.Completed);
            _extractor.ScriptResult(embedding);
            Assert.That(
                _service.CaptureRegistrationView(_frame),
                Is.EqualTo(RegistrationCaptureStatus.Started));
            Assert.That(_service.Tick(0d).Status, Is.EqualTo(RecognitionTickStatus.CaptureAccepted));
        }

        private static float[] Unit(float x, float y)
        {
            return new[] { x, y };
        }

        private static float[] Normalize(float x, float y)
        {
            var norm = (float)Math.Sqrt((x * x) + (y * y));
            return new[] { x / norm, y / norm };
        }
    }
}
