using System;
using System.Collections.Generic;
using ObjecTalk.Core.Recognition;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using ObjecTalk.Infrastructure.Database;
using ObjecTalk.Infrastructure.Vision;
using Unity.InferenceEngine;
using UnityEngine;

namespace ObjecTalk.Tests
{
    /// <summary>
    /// An extractor whose every step is scripted, so the orchestrator's state machine can be
    /// driven through completion, failure, and cancellation without a model or a graphics device.
    /// </summary>
    public sealed class FakeEmbeddingExtractor : IEmbeddingExtractor
    {
        private readonly Queue<EmbeddingStepResult> _scriptedSteps = new Queue<EmbeddingStepResult>();
        private float[] _result;
        private bool _busy;

        public FakeEmbeddingExtractor(int embeddingDimension = 2)
        {
            EmbeddingDimension = embeddingDimension;
        }

        public EmbeddingBackend Backend { get; set; } = EmbeddingBackend.Cpu;

        public bool IsDisabled { get; set; }

        public string FailureMessage { get; set; } = string.Empty;

        public bool IsBusy => _busy;

        public int Generation { get; private set; }

        public int EmbeddingDimension { get; }

        public int BeginCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int LastCancelledGeneration { get; private set; } = -1;

        public Texture LastInput { get; private set; }

        /// <summary>Refuses every <see cref="Begin"/>, standing in for an unavailable worker.</summary>
        public bool RefuseBegin { get; set; }

        public void ScriptSteps(params EmbeddingStepResult[] steps)
        {
            _scriptedSteps.Clear();
            foreach (var step in steps)
            {
                _scriptedSteps.Enqueue(step);
            }
        }

        /// <summary>The embedding a completed run publishes. Must already be L2-normalized.</summary>
        public void ScriptResult(params float[] embedding)
        {
            _result = embedding;
        }

        public bool Initialize(ModelAsset modelAsset, EmbeddingModelSpec spec)
        {
            return !IsDisabled;
        }

        public bool Begin(Texture input, int generation)
        {
            if (_busy || IsDisabled || RefuseBegin)
            {
                return false;
            }

            _busy = true;
            Generation = generation;
            LastInput = input;
            BeginCount++;
            return true;
        }

        public EmbeddingStepResult Step()
        {
            if (!_busy)
            {
                return EmbeddingStepResult.Idle;
            }

            var step = _scriptedSteps.Count > 0
                ? _scriptedSteps.Dequeue()
                : EmbeddingStepResult.Completed;

            if (step != EmbeddingStepResult.Pending)
            {
                _busy = false;
            }

            return step;
        }

        public bool CopyNormalizedResult(Span<float> destination)
        {
            if (_result == null || destination.Length < _result.Length)
            {
                return false;
            }

            for (var i = 0; i < _result.Length; i++)
            {
                destination[i] = _result[i];
            }

            return true;
        }

        public void Cancel(int generation)
        {
            LastCancelledGeneration = generation;
            _busy = false;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    public sealed class FakeFramePreprocessor : IFramePreprocessor
    {
        private Texture2D _verificationFrame;

        public int EmbeddingSize => ImageGeometry.EmbeddingImageSize;

        public int ReleaseBufferCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int VerificationCopyCount { get; private set; }

        public Texture PrepareEmbedding(Texture source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return source;
        }

        public Texture2D CopyVerificationFrame(Texture source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            VerificationCopyCount++;
            if (_verificationFrame == null)
            {
                _verificationFrame = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            }

            return _verificationFrame;
        }

        public void ReleaseBuffers()
        {
            ReleaseBufferCount++;
            DestroyFrame();
        }

        public void Dispose()
        {
            DisposeCount++;
            DestroyFrame();
        }

        private void DestroyFrame()
        {
            if (_verificationFrame == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(_verificationFrame);
            _verificationFrame = null;
        }
    }

    public sealed class FakeLocalFeatureSample : ILocalFeatureSample
    {
        public FakeLocalFeatureSample(string objectId, bool hasFeatures = true)
        {
            ObjectId = objectId;
            HasFeatures = hasFeatures;
        }

        public string ObjectId { get; }

        public bool HasFeatures { get; private set; }

        public bool IsDisposed { get; private set; }

        public LocalFeatureBlob Serialize()
        {
            return new LocalFeatureBlob(
                new byte[] { 1, 2, 3, 4 },
                new byte[] { 5, 6, 7, 8 },
                1,
                4,
                0,
                64,
                64);
        }

        public void Dispose()
        {
            IsDisposed = true;
            HasFeatures = false;
        }
    }

    public sealed class FakeLocalFeatureVerifier : ILocalFeatureVerifier
    {
        private readonly List<ILocalFeatureSample> _verified = new List<ILocalFeatureSample>();

        public int InitializeCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int VerifyCallCount { get; private set; }

        public LocalVerificationThresholds LastThresholds { get; private set; }

        public IReadOnlyList<ILocalFeatureSample> VerifiedAgainst => _verified;

        /// <summary>What <see cref="Verify"/> answers for every reference.</summary>
        public bool VerifyResult { get; set; } = true;

        /// <summary>When set, a confirmed reference also reports this 0..1 location.</summary>
        public (float x, float y)? VerifyLocation { get; set; }

        /// <summary>What a registration capture yields; false makes the capture featureless.</summary>
        public bool ExtractHasFeatures { get; set; } = true;

        /// <summary>When set, <see cref="Restore"/> returns null, standing in for a corrupt row.</summary>
        public bool RestoreFails { get; set; }

        public void Initialize()
        {
            InitializeCount++;
        }

        public ILocalFeatureSample Extract(Texture2D image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            return new FakeLocalFeatureSample("capture", ExtractHasFeatures);
        }

        public ILocalFeatureSample Restore(LocalFeatureBlob blob)
        {
            return RestoreFails ? null : new FakeLocalFeatureSample("restored");
        }

        public LocalVerificationResult Verify(
            Texture2D image, ILocalFeatureSample reference, in LocalVerificationThresholds thresholds)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            VerifyCallCount++;
            LastThresholds = thresholds;
            _verified.Add(reference);

            if (!VerifyResult)
            {
                return LocalVerificationResult.Rejected;
            }

            return VerifyLocation.HasValue
                ? LocalVerificationResult.VerifiedAt(VerifyLocation.Value.x, VerifyLocation.Value.y)
                : LocalVerificationResult.Verified();
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    public sealed class FakeRecognitionSampleStore : IRecognitionSampleStore
    {
        private readonly List<RecognitionSampleRecord> _records = new List<RecognitionSampleRecord>();

        public int InvalidateCount { get; private set; }

        public int SaveCount { get; private set; }

        public string LastSavedObjectId { get; private set; }

        public IReadOnlyList<RecognitionSampleRecord> LastSavedSamples { get; private set; }

        /// <summary>When set, <see cref="SaveSamples"/> throws, standing in for a failed write.</summary>
        public bool FailSave { get; set; }

        public void Seed(params RecognitionSampleRecord[] records)
        {
            _records.Clear();
            _records.AddRange(records);
        }

        public IReadOnlyList<RecognitionSampleRecord> LoadValidSamples(
            string userId,
            RecognitionDataVersion expected)
        {
            return _records;
        }

        public void SaveSamples(string objectId, IReadOnlyList<RecognitionSampleRecord> samples)
        {
            if (FailSave)
            {
                throw new InvalidOperationException("The store rejected the write.");
            }

            SaveCount++;
            LastSavedObjectId = objectId;
            LastSavedSamples = samples;
        }

        public void DeleteSamples(string objectId)
        {
        }

        public void InvalidateObsolete(RecognitionDataVersion expected)
        {
            InvalidateCount++;
        }
    }
}
