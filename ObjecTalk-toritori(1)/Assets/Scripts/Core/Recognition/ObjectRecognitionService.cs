using System;
using System.Collections.Generic;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using ObjecTalk.Infrastructure.Vision;
using UnityEngine;

namespace ObjecTalk.Core.Recognition
{
    /// <summary>
    /// Owns one asynchronous recognition pipeline: it schedules inference, screens the result
    /// against the registered samples, confirms the single best candidate with local features,
    /// stabilizes the outcome over time, and drives three-view registration.
    ///
    /// It deliberately knows nothing about UI, SQLite, the camera capture API, or scenes. Every
    /// collaborator arrives through an interface, and every threshold comes from the injected
    /// <see cref="RecognitionSettings"/> rather than from a constant at a call site.
    ///
    /// The service owns its collaborators' lifetimes: <see cref="Dispose"/> releases the extractor,
    /// the preprocessor, the verifier, and every restored local-feature sample.
    /// </summary>
    public sealed class ObjectRecognitionService : IDisposable
    {
        private enum PendingKind
        {
            None = 0,
            Recognition = 1,
            Registration = 2
        }

        private readonly IEmbeddingExtractor _extractor;
        private readonly IFramePreprocessor _preprocessor;
        private readonly ILocalFeatureVerifier _verifier;
        private readonly IRecognitionSampleStore _store;
        private readonly RecognitionSettings _settings;
        private readonly RecognitionDataVersion _dataVersion;
        private readonly AdaptiveRecognitionScheduler _scheduler;

        private readonly List<RecognitionSample> _samples = new List<RecognitionSample>();
        private readonly Dictionary<string, List<ILocalFeatureSample>> _references =
            new Dictionary<string, List<ILocalFeatureSample>>(StringComparer.Ordinal);
        private readonly List<PendingCapture> _captures = new List<PendingCapture>();

        private TemporalRecognitionFilter _filter;
        private float[] _embedding;

        private PendingKind _pendingKind;
        private int _pendingGeneration;
        private int _pendingSchedulerToken;
        private ILocalFeatureSample _pendingFeatures;
        private Texture _activeFrame;
        private double _runStartedSeconds;
        private int _runGeneration;

        private string _registrationObjectId;
        private string _recognizedObjectId;
        private bool _paused;
        private bool _disposed;

        public ObjectRecognitionService(
            IEmbeddingExtractor extractor,
            IFramePreprocessor preprocessor,
            ILocalFeatureVerifier verifier,
            IRecognitionSampleStore store,
            RecognitionSettings settings,
            RecognitionDataVersion dataVersion)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _store = store ?? throw new ArgumentNullException(nameof(store));

            if (settings.MinimumLocalMatchCount < 1)
            {
                throw new ArgumentException(
                    "Recognition settings must carry a positive minimum local match count.",
                    nameof(settings));
            }

            _settings = settings;
            _dataVersion = dataVersion;
            _scheduler = new AdaptiveRecognitionScheduler(settings);
            _filter = new TemporalRecognitionFilter(settings);
        }

        /// <summary>Raised only when the stabilized recognition state changes.</summary>
        public event Action<RecognitionStateChange> RecognitionStateChanged;

        /// <summary>The settings every threshold in this pipeline comes from.</summary>
        public RecognitionSettings Settings => _settings;

        public bool IsPaused => _paused;

        /// <summary>True while a run is in flight, so no second run can start.</summary>
        public bool IsBusy => _pendingKind != PendingKind.None;

        public bool IsRegistering => _registrationObjectId != null;

        /// <summary>Registration captures kept so far; three are required to commit.</summary>
        public int CapturedViewCount => _captures.Count;

        /// <summary>The stabilized object, or null when nothing is recognized.</summary>
        public string RecognizedObjectId => _recognizedObjectId;

        /// <summary>Distinct objects currently available for matching.</summary>
        public int RegisteredObjectCount => _references.Count;

        /// <summary>Loaded samples across all objects.</summary>
        public int LoadedSampleCount => _samples.Count;

        /// <summary>
        /// Discards samples written by another model or preprocessing version, then loads what
        /// remains for <paramref name="userId"/>. A sample whose embedding or local features
        /// cannot be trusted is skipped without failing the rest of the load.
        /// </summary>
        public void LoadSamples(string userId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("A user id is required.", nameof(userId));
            }

            _store.InvalidateObsolete(_dataVersion);
            ClearLoadedSamples();

            var records = _store.LoadValidSamples(userId, _dataVersion);
            if (records == null)
            {
                return;
            }

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null || string.IsNullOrEmpty(record.ObjectId))
                {
                    continue;
                }

                RecognitionSample sample;
                ILocalFeatureSample features;
                try
                {
                    sample = new RecognitionSample(record.ObjectId, record.Embedding);
                    features = _verifier.Restore(new LocalFeatureBlob(
                        record.KeypointBlob,
                        record.DescriptorBlob,
                        record.DescriptorRows,
                        record.DescriptorColumns,
                        record.DescriptorType,
                        record.ImageWidth,
                        record.ImageHeight));
                }
                catch (ArgumentException)
                {
                    // A row that survived the repository's checks can still be unusable here;
                    // skipping it keeps every other object loadable.
                    continue;
                }

                if (features == null || !features.HasFeatures)
                {
                    features?.Dispose();
                    continue;
                }

                _samples.Add(sample);
                AddReference(record.ObjectId, features);
            }
        }

        /// <summary>
        /// Starts one recognition cycle when the cadence allows it and nothing else is running.
        /// Returns false whenever a frame must simply be skipped, which is the common case.
        /// </summary>
        public bool StartRecognition(Texture frame, double now)
        {
            ThrowIfDisposed();
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (_paused || IsRegistering || IsBusy || _extractor.IsDisabled || _samples.Count == 0)
            {
                return false;
            }

            var token = _scheduler.TryBegin(now);
            if (!token.HasValue)
            {
                return false;
            }

            var prepared = _preprocessor.PrepareEmbedding(frame);
            _runGeneration++;
            if (!_extractor.Begin(prepared, _runGeneration))
            {
                _scheduler.Cancel();
                return false;
            }

            _pendingKind = PendingKind.Recognition;
            _pendingGeneration = _runGeneration;
            _pendingSchedulerToken = token.Value;
            // Held by reference, not copied: a camera texture updates in place, so verification
            // later in this cycle sees the current frame rather than a stale snapshot.
            _activeFrame = frame;
            _runStartedSeconds = now;
            return true;
        }

        /// <summary>Advances whichever run is in flight by one bounded slice of work.</summary>
        public RecognitionTickResult Tick(double now)
        {
            ThrowIfDisposed();
            if (_paused || _pendingKind == PendingKind.None)
            {
                return Result(RecognitionTickStatus.Idle);
            }

            var step = _extractor.Step();
            switch (step)
            {
                case EmbeddingStepResult.Pending:
                    return Result(RecognitionTickStatus.Pending);

                case EmbeddingStepResult.Completed:
                    return Publish(now);

                case EmbeddingStepResult.Failed:
                    return Fail(now);

                default:
                    // The extractor reports Idle only if the run was cancelled underneath us.
                    EndRun();
                    return Result(RecognitionTickStatus.Idle);
            }
        }

        /// <summary>
        /// Opens registration for <paramref name="objectId"/>, discarding any captures in progress
        /// and stopping recognition for its duration.
        /// </summary>
        public void BeginRegistration(string objectId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("An object id is required.", nameof(objectId));
            }

            CancelRegistration();
            CancelPendingRun();
            ResetStabilizedState();
            _registrationObjectId = objectId;
        }

        /// <summary>
        /// Extracts local features from <paramref name="frame"/> immediately and, when they are
        /// usable, starts the embedding run whose result <see cref="Tick"/> will keep. A featureless
        /// frame is refused straight away so the UI can ask for another angle.
        /// </summary>
        public RegistrationCaptureStatus CaptureRegistrationView(Texture frame)
        {
            ThrowIfDisposed();
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!IsRegistering || _captures.Count >= RecognitionSettings.RequiredSamplesPerObject)
            {
                return RegistrationCaptureStatus.NotAccepting;
            }

            if (_paused || IsBusy || _extractor.IsDisabled)
            {
                return RegistrationCaptureStatus.Unavailable;
            }

            var verificationFrame = _preprocessor.CopyVerificationFrame(frame);
            var features = _verifier.Extract(verificationFrame);
            if (features == null || !features.HasFeatures)
            {
                features?.Dispose();
                return RegistrationCaptureStatus.Featureless;
            }

            var prepared = _preprocessor.PrepareEmbedding(frame);
            _runGeneration++;
            if (!_extractor.Begin(prepared, _runGeneration))
            {
                features.Dispose();
                return RegistrationCaptureStatus.Unavailable;
            }

            _pendingKind = PendingKind.Registration;
            _pendingGeneration = _runGeneration;
            _pendingFeatures = features;
            return RegistrationCaptureStatus.Started;
        }

        /// <summary>
        /// Commits the three captures as one object. Returns false unless exactly three valid
        /// captures exist, so a partial registration can never reach the database.
        /// </summary>
        public bool CompleteRegistration()
        {
            ThrowIfDisposed();
            if (!IsRegistering || _captures.Count != RecognitionSettings.RequiredSamplesPerObject)
            {
                return false;
            }

            var objectId = _registrationObjectId;
            var records = new RecognitionSampleRecord[RecognitionSettings.RequiredSamplesPerObject];
            for (var i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
                var blob = capture.Features.Serialize();
                records[i] = new RecognitionSampleRecord(
                    i,
                    _dataVersion,
                    capture.Embedding,
                    blob.Keypoints,
                    blob.DescriptorRows,
                    blob.DescriptorColumns,
                    blob.DescriptorType,
                    blob.Descriptors,
                    blob.ImageWidth,
                    blob.ImageHeight);
            }

            _store.SaveSamples(objectId, records);

            // Only after the store accepted all three does the in-memory index change, so a failed
            // write leaves the service matching exactly what is persisted.
            RemoveObject(objectId);
            for (var i = 0; i < _captures.Count; i++)
            {
                var capture = _captures[i];
                _samples.Add(new RecognitionSample(objectId, capture.Embedding));
                AddReference(objectId, capture.Features);
            }

            _captures.Clear();
            _registrationObjectId = null;
            return true;
        }

        /// <summary>Abandons an open registration and every capture it collected.</summary>
        public void CancelRegistration()
        {
            ThrowIfDisposed();
            if (_pendingKind == PendingKind.Registration)
            {
                CancelPendingRun();
            }

            for (var i = 0; i < _captures.Count; i++)
            {
                _captures[i].Features?.Dispose();
            }

            _captures.Clear();
            _registrationObjectId = null;
        }

        /// <summary>
        /// Stops accepting frames, abandons the run in flight so its result can never be published,
        /// and hands the preprocessor's graphics memory back.
        /// </summary>
        public void Pause()
        {
            ThrowIfDisposed();
            if (_paused)
            {
                return;
            }

            _paused = true;
            CancelPendingRun();
            ResetStabilizedState();
            _preprocessor.ReleaseBuffers();
        }

        /// <summary>Accepts frames again on a generation no in-flight result can claim.</summary>
        public void Resume()
        {
            ThrowIfDisposed();
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _runGeneration++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _pendingFeatures?.Dispose();
            _pendingFeatures = null;
            for (var i = 0; i < _captures.Count; i++)
            {
                _captures[i].Features?.Dispose();
            }

            _captures.Clear();
            _registrationObjectId = null;
            _activeFrame = null;
            _pendingKind = PendingKind.None;

            DisposeReferences();
            _samples.Clear();

            _extractor.Dispose();
            _preprocessor.Dispose();
            _verifier.Dispose();
        }

        private RecognitionTickResult Publish(double now)
        {
            var kind = _pendingKind;
            EnsureEmbeddingBuffer();
            var copied = _embedding != null && _extractor.CopyNormalizedResult(_embedding);
            if (!copied)
            {
                return Fail(now);
            }

            if (kind == PendingKind.Registration)
            {
                var features = _pendingFeatures;
                _pendingFeatures = null;
                EndRun();
                _captures.Add(new PendingCapture((float[])_embedding.Clone(), features));
                return Result(RecognitionTickStatus.CaptureAccepted);
            }

            var candidate = EmbeddingMath.FindBest(
                _samples,
                _embedding,
                _settings.SimilarityThreshold,
                _settings.AmbiguityMargin);

            // Local verification is the expensive half, so it runs for the one candidate that
            // already cleared the neural threshold and the ambiguity margin, and for nothing else.
            var verification = candidate.HasValue
                ? VerifyLocally(candidate.Value.Label)
                : LocalVerificationResult.Rejected;
            var verified = verification.IsVerified ? candidate : null;

            var stabilized = _filter.Observe(verified, now);
            _scheduler.Complete(_pendingSchedulerToken, now, now - _runStartedSeconds);
            EndRun();
            PublishStateChange(stabilized);

            return stabilized.HasValue
                ? new RecognitionTickResult(
                    RecognitionTickStatus.Recognized,
                    stabilized.Value.Label,
                    stabilized.Value.Score,
                    _captures.Count,
                    verification.HasLocation,
                    verification.RelativeX,
                    verification.RelativeY)
                : Result(RecognitionTickStatus.NotRecognized);
        }

        private RecognitionTickResult Fail(double now)
        {
            var kind = _pendingKind;
            var schedulerToken = _pendingSchedulerToken;
            var startedSeconds = _runStartedSeconds;

            _extractor.Cancel(_pendingGeneration);
            _pendingFeatures?.Dispose();
            _pendingFeatures = null;
            EndRun();

            if (kind == PendingKind.Recognition)
            {
                var stabilized = _filter.Observe(null, now);
                _scheduler.Complete(schedulerToken, now, now - startedSeconds);
                PublishStateChange(stabilized);
            }

            return Result(RecognitionTickStatus.Failed);
        }

        private LocalVerificationResult VerifyLocally(string objectId)
        {
            if (_activeFrame == null || !_references.TryGetValue(objectId, out var references))
            {
                return LocalVerificationResult.Rejected;
            }

            var verificationFrame = _preprocessor.CopyVerificationFrame(_activeFrame);
            for (var i = 0; i < references.Count; i++)
            {
                var verification = _verifier.Verify(
                    verificationFrame, references[i], _settings.MinimumLocalMatchCount);
                if (verification.IsVerified)
                {
                    return verification;
                }
            }

            return LocalVerificationResult.Rejected;
        }

        private void PublishStateChange(RecognitionCandidate? stabilized)
        {
            var objectId = stabilized?.Label;
            if (string.Equals(objectId, _recognizedObjectId, StringComparison.Ordinal))
            {
                return;
            }

            _recognizedObjectId = objectId;
            RecognitionStateChanged?.Invoke(new RecognitionStateChange(objectId, stabilized?.Score ?? 0f));
        }

        private void ResetStabilizedState()
        {
            // A fresh filter is the cheapest way to guarantee no evidence survives the transition;
            // it happens on pause and registration, never inside the steady-state loop.
            _filter = new TemporalRecognitionFilter(_settings);
            PublishStateChange(null);
        }

        private void CancelPendingRun()
        {
            if (_pendingKind == PendingKind.None)
            {
                return;
            }

            if (_pendingKind == PendingKind.Recognition)
            {
                _scheduler.Cancel();
            }

            _extractor.Cancel(_pendingGeneration);
            _pendingFeatures?.Dispose();
            _pendingFeatures = null;
            EndRun();
        }

        private void EndRun()
        {
            _pendingKind = PendingKind.None;
            _pendingGeneration = 0;
            _pendingSchedulerToken = 0;
            _activeFrame = null;
        }

        private void EnsureEmbeddingBuffer()
        {
            var dimension = _extractor.EmbeddingDimension;
            if (dimension <= 0)
            {
                return;
            }

            if (_embedding == null || _embedding.Length != dimension)
            {
                _embedding = new float[dimension];
            }
        }

        private void AddReference(string objectId, ILocalFeatureSample features)
        {
            if (!_references.TryGetValue(objectId, out var list))
            {
                list = new List<ILocalFeatureSample>(RecognitionSettings.RequiredSamplesPerObject);
                _references.Add(objectId, list);
            }

            list.Add(features);
        }

        private void RemoveObject(string objectId)
        {
            for (var i = _samples.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_samples[i].Label, objectId, StringComparison.Ordinal))
                {
                    _samples.RemoveAt(i);
                }
            }

            if (!_references.TryGetValue(objectId, out var list))
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                list[i]?.Dispose();
            }

            _references.Remove(objectId);
        }

        private void ClearLoadedSamples()
        {
            DisposeReferences();
            _samples.Clear();
        }

        private void DisposeReferences()
        {
            foreach (var pair in _references)
            {
                var list = pair.Value;
                for (var i = 0; i < list.Count; i++)
                {
                    list[i]?.Dispose();
                }
            }

            _references.Clear();
        }

        private RecognitionTickResult Result(RecognitionTickStatus status)
        {
            return new RecognitionTickResult(status, null, 0f, _captures.Count);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ObjectRecognitionService));
            }
        }

        private readonly struct PendingCapture
        {
            public PendingCapture(float[] embedding, ILocalFeatureSample features)
            {
                Embedding = embedding;
                Features = features;
            }

            public float[] Embedding { get; }

            public ILocalFeatureSample Features { get; }
        }
    }
}
