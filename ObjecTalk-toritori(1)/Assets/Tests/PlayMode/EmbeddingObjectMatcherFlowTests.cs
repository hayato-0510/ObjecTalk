using System.Collections.Generic;
using NUnit.Framework;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using SQLite;
using UnityEngine;

namespace ObjecTalk.Tests.PlayMode
{
    /// <summary>
    /// The registration and recognition flow the camera panel drives, exercised against a real
    /// <see cref="RecognitionSampleRepository"/> on an in-memory database.
    ///
    /// The controller itself is a MonoBehaviour wired to scene panels, singletons, and a live
    /// camera, so what is verified here is the flow it delegates to: how many captures commit,
    /// what a retry does, what a cancel leaves behind, and that a commit is one transaction.
    /// </summary>
    [TestFixture]
    public sealed class EmbeddingObjectMatcherFlowTests
    {
        private const string UserId = "user-1";
        private const string ObjectId = "object-a";

        private static readonly RecognitionDataVersion CurrentVersion = new RecognitionDataVersion(
            1,
            EmbeddingModelSpec.Default.ModelVersion,
            EmbeddingModelSpec.Default.PreprocessingVersion);

        private SQLiteConnection _connection;
        private RecognitionSampleRepository _repository;
        private FakeEmbeddingExtractor _extractor;
        private FakeFramePreprocessor _preprocessor;
        private FakeLocalFeatureVerifier _verifier;
        private ObjectRecognitionService _service;
        private Texture2D _frame;

        [SetUp]
        public void SetUp()
        {
            _connection = new SQLiteConnection(":memory:");
            CreateObjectsTable();
            _repository = new RecognitionSampleRepository(_connection);

            _extractor = new FakeEmbeddingExtractor();
            _preprocessor = new FakeFramePreprocessor();
            _verifier = new FakeLocalFeatureVerifier();
            _frame = new Texture2D(64, 48, TextureFormat.RGBA32, false, false);

            _service = new ObjectRecognitionService(
                _extractor,
                _preprocessor,
                _verifier,
                _repository,
                Settings(),
                CurrentVersion);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
            _repository?.Dispose();
            _repository = null;
            _connection?.Dispose();
            _connection = null;
            if (_frame != null)
            {
                Object.DestroyImmediate(_frame);
                _frame = null;
            }
        }

        [Test]
        public void ThreeCapturesAreRequiredBeforeAnythingIsPersisted()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            _service.BeginRegistration(ObjectId);

            Capture(1f, 0f);
            Assert.That(_service.CompleteRegistration(), Is.False);
            Assert.That(CountPersistedSamples(), Is.Zero);

            Capture(0f, 1f);
            Assert.That(_service.CompleteRegistration(), Is.False);
            Assert.That(CountPersistedSamples(), Is.Zero);

            Capture(0.6f, 0.8f);
            Assert.That(_service.CompleteRegistration(), Is.True);
            Assert.That(CountPersistedSamples(), Is.EqualTo(RecognitionSettings.RequiredSamplesPerObject));
        }

        [Test]
        public void AFeaturelessCaptureAsksForARetryAndDoesNotIncrementTheCount()
        {
            _service.BeginRegistration(ObjectId);
            _verifier.ExtractHasFeatures = false;

            var status = _service.CaptureRegistrationView(_frame);

            Assert.That(status, Is.EqualTo(RegistrationCaptureStatus.Featureless));
            Assert.That(_service.CapturedViewCount, Is.Zero);
            Assert.That(
                RecognitionMessages.RegistrationProgress(0, RecognitionSettings.RequiredSamplesPerObject),
                Does.Contain("0/3"));
        }

        [Test]
        public void CancellingRegistrationDiscardsEveryCaptureAndPersistsNothing()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            _service.BeginRegistration(ObjectId);
            Capture(1f, 0f);
            Capture(0f, 1f);

            _service.CancelRegistration();

            Assert.That(_service.IsRegistering, Is.False);
            Assert.That(_service.CapturedViewCount, Is.Zero);
            Assert.That(CountPersistedSamples(), Is.Zero);
            Assert.That(_service.CompleteRegistration(), Is.False);
        }

        [Test]
        public void ACommittedRegistrationBecomesRecognizableImmediately()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            _service.BeginRegistration(ObjectId);
            Capture(1f, 0f);
            Capture(0f, 1f);
            Capture(0.6f, 0.8f);

            Assert.That(_service.CompleteRegistration(), Is.True);
            Assert.That(_service.RegisteredObjectCount, Is.EqualTo(1));

            _extractor.ScriptResult(1f, 0f);
            Assert.That(_service.StartRecognition(_frame, 10d), Is.True);
            var result = _service.Tick(10.05d);

            Assert.That(result.Status, Is.EqualTo(RecognitionTickStatus.Recognized));
            Assert.That(result.ObjectId, Is.EqualTo(ObjectId));
        }

        [Test]
        public void SamplesSurviveAReloadFromTheDatabase()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            _service.BeginRegistration(ObjectId);
            Capture(1f, 0f);
            Capture(0f, 1f);
            Capture(0.6f, 0.8f);
            Assert.That(_service.CompleteRegistration(), Is.True);

            _service.LoadSamples(UserId);

            Assert.That(_service.LoadedSampleCount, Is.EqualTo(RecognitionSettings.RequiredSamplesPerObject));
            Assert.That(_service.RegisteredObjectCount, Is.EqualTo(1));
        }

        [Test]
        public void SamplesFromAnotherModelVersionAreDiscardedAndTheObjectSurvives()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            var obsolete = new RecognitionDataVersion(1, "older-model", "older-preprocessing");
            _repository.SaveSamples(ObjectId, new[]
            {
                ObsoleteRecord(0, obsolete),
                ObsoleteRecord(1, obsolete),
                ObsoleteRecord(2, obsolete)
            });
            Assert.That(CountPersistedSamples(), Is.EqualTo(3));

            _service.LoadSamples(UserId);

            Assert.That(_service.RegisteredObjectCount, Is.Zero, "Obsolete features must not match.");
            Assert.That(CountPersistedSamples(), Is.Zero, "Obsolete rows must be removed.");
            Assert.That(
                _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM objects WHERE object_id = ?", ObjectId),
                Is.EqualTo(1),
                "The object itself, its name and its placement must survive.");
            Assert.That(RecognitionMessages.ReRegistrationRequired, Does.Contain("再登録"));
        }

        [Test]
        public void RegisteringTheSameObjectAgainReplacesItsSamples()
        {
            InsertObjectRow(ObjectId, "マグカップ");
            RegisterThreeViews(1f, 0f);
            Assert.That(CountPersistedSamples(), Is.EqualTo(3));

            RegisterThreeViews(0f, 1f);

            Assert.That(CountPersistedSamples(), Is.EqualTo(3), "A re-registration must not accumulate rows.");
            Assert.That(_service.RegisteredObjectCount, Is.EqualTo(1));
            Assert.That(_service.LoadedSampleCount, Is.EqualTo(3));
        }

        [Test]
        public void PausingDuringRegistrationRefusesFurtherCaptures()
        {
            _service.BeginRegistration(ObjectId);
            Capture(1f, 0f);

            _service.Pause();

            Assert.That(
                _service.CaptureRegistrationView(_frame),
                Is.EqualTo(RegistrationCaptureStatus.Unavailable));
            Assert.That(_service.CapturedViewCount, Is.EqualTo(1), "A pause must not lose captures already taken.");

            _service.Resume();
            Capture(0f, 1f);
            Assert.That(_service.CapturedViewCount, Is.EqualTo(2));
        }

        [Test]
        public void TheProgressMessagesAreJapaneseAndCountUp()
        {
            Assert.That(RecognitionMessages.RegistrationProgress(1, 3), Does.Contain("1/3"));
            Assert.That(RecognitionMessages.RegistrationProgress(2, 3), Does.Contain("2/3"));
            Assert.That(
                RecognitionMessages.RegistrationProgress(3, 3),
                Is.EqualTo(RecognitionMessages.RegistrationComplete));
            Assert.That(RecognitionMessages.RegistrationRetry, Does.Contain("もう一度"));
            Assert.That(RecognitionMessages.CameraPermissionDenied, Does.Contain("カメラ"));
        }

        // --- helpers --------------------------------------------------------------------------

        private static RecognitionSettings Settings()
        {
            return new RecognitionSettings(
                0.7f,
                0.05f,
                1,
                RecognitionSettings.DefaultLossGracePeriodSeconds,
                RecognitionSettings.MinimumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.MaximumSupportedRecognitionIntervalSeconds,
                RecognitionSettings.DefaultMinimumLocalMatchCount);
        }

        private void RegisterThreeViews(float x, float y)
        {
            _service.BeginRegistration(ObjectId);
            Capture(x, y);
            Capture(y, x);
            Capture(0.6f, 0.8f);
            Assert.That(_service.CompleteRegistration(), Is.True);
        }

        private void Capture(float x, float y)
        {
            _extractor.ScriptSteps(EmbeddingStepResult.Completed);
            _extractor.ScriptResult(x, y);
            Assert.That(
                _service.CaptureRegistrationView(_frame),
                Is.EqualTo(RegistrationCaptureStatus.Started));
            Assert.That(_service.Tick(0d).Status, Is.EqualTo(RecognitionTickStatus.CaptureAccepted));
        }

        private RecognitionSampleRecord ObsoleteRecord(int index, RecognitionDataVersion version)
        {
            return new RecognitionSampleRecord(
                index,
                version,
                new[] { 1f, 0f },
                new byte[] { 1, 2, 3, 4 },
                1,
                4,
                0,
                new byte[] { 5, 6, 7, 8 },
                64,
                64);
        }

        private int CountPersistedSamples()
        {
            return _connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM recognition_samples WHERE object_id = ?",
                ObjectId);
        }

        /// <summary>
        /// The repository joins the application's objects table, so the flow tests create the
        /// columns it reads rather than depending on the application's own migration order.
        /// </summary>
        private void CreateObjectsTable()
        {
            _connection.Execute(
                "CREATE TABLE IF NOT EXISTS objects ("
                + "object_id TEXT PRIMARY KEY, "
                + "user_id TEXT NOT NULL, "
                + "name TEXT, "
                + "feature_vector_blob BLOB, "
                + "keypoints_blob BLOB, "
                + "descriptors_blob BLOB)");
            _connection.Execute(
                "CREATE TABLE IF NOT EXISTS object_features ("
                + "object_id TEXT NOT NULL, "
                + "feature_id INTEGER NOT NULL, "
                + "feature_vector_blob BLOB)");
        }

        private void InsertObjectRow(string objectId, string name)
        {
            _connection.Execute(
                "INSERT INTO objects (object_id, user_id, name) VALUES (?, ?, ?)",
                objectId,
                UserId,
                name);
        }
    }
}
