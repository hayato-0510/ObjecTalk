using System;
using System.Collections;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;
using ObjecTalk.Infrastructure.AI;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

// Brings in AllocatingGCMemory() while keeping every other constraint on NUnit's Is.
using Is = NUnit.Framework.Is;

namespace ObjecTalk.Tests.PlayMode
{
    /// <summary>
    /// Exercises the extractor against the model committed under <c>Assets/Models</c>. The asset is
    /// resolved through the AssetDatabase, so the fixture is skipped in a player build.
    /// </summary>
    public class SentisEmbeddingExtractorTests
    {
        private const string ModelPath = "Assets/Models/mobilenetv3-small-embedding-fp16.onnx";
        private const int MaximumSteps = 2000;

        private ModelAsset _modelAsset;
        private SentisEmbeddingExtractor _extractor;
        private Texture2D _frame;

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            _modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
#endif
            if (_modelAsset == null)
            {
                Assert.Ignore($"The embedding model at {ModelPath} is only reachable in the Editor.");
            }

            _frame = CreateFrame(ImageGeometry.EmbeddingImageSize);
            _extractor = new SentisEmbeddingExtractor();
            Assert.That(
                _extractor.Initialize(_modelAsset, EmbeddingModelSpec.Default),
                Is.True,
                _extractor.FailureMessage);
        }

        [TearDown]
        public void TearDown()
        {
            _extractor?.Dispose();
            _extractor = null;
            if (_frame != null)
            {
                UnityEngine.Object.Destroy(_frame);
                _frame = null;
            }
        }

        [Test]
        public void Initialize_SelectsABackendAndReportsTheModelDimension()
        {
            Assert.That(_extractor.Backend, Is.Not.EqualTo(EmbeddingBackend.None));
            Assert.That(_extractor.IsDisabled, Is.False);
            Assert.That(_extractor.EmbeddingDimension, Is.EqualTo(576));
            Assert.That(_extractor.IsBusy, Is.False);
        }

        [Test]
        public void Step_ReportsIdleBeforeAnyRun()
        {
            Assert.That(_extractor.Step(), Is.EqualTo(EmbeddingStepResult.Idle));
        }

        [Test]
        public void Begin_RejectsAMissingFrame()
        {
            Assert.Throws<ArgumentNullException>(() => _extractor.Begin(null, 1));
        }

        [Test]
        public void Begin_IsRejectedWhileARunIsInFlight()
        {
            Assert.That(_extractor.Begin(_frame, 1), Is.True);
            Assert.That(_extractor.IsBusy, Is.True);

            Assert.That(_extractor.Begin(_frame, 2), Is.False, "a second run must not overlap the first");
            Assert.That(_extractor.Generation, Is.EqualTo(1));
        }

        [Test]
        public void Step_SchedulesIncrementallyRatherThanRunningTheWholeModelAtOnce()
        {
            Assert.That(_extractor.Begin(_frame, 1), Is.True);

            Assert.That(
                _extractor.Step(),
                Is.EqualTo(EmbeddingStepResult.Pending),
                "MobileNetV3-Small has far more layers than one step schedules");
            Assert.That(_extractor.IsBusy, Is.True);
        }

        [UnityTest]
        public IEnumerator Run_ProducesAFiniteUnitLengthEmbedding()
        {
            Assert.That(_extractor.Begin(_frame, 5), Is.True);
            yield return RunToCompletion();

            var embedding = new float[_extractor.EmbeddingDimension];
            Assert.That(_extractor.CopyNormalizedResult(embedding), Is.True);

            var squaredNorm = 0d;
            foreach (var value in embedding)
            {
                Assert.That(float.IsFinite(value), Is.True, "every embedding value must be finite");
                squaredNorm += (double)value * value;
            }

            Assert.That(Math.Sqrt(squaredNorm), Is.EqualTo(1.0).Within(1e-4));
            Assert.That(_extractor.Generation, Is.EqualTo(5));
            Assert.That(_extractor.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator CopyNormalizedResult_ReusesItsBufferAndDoesNotAllocate()
        {
            Assert.That(_extractor.Begin(_frame, 1), Is.True);
            yield return RunToCompletion();

            var destination = new float[_extractor.EmbeddingDimension];
            _extractor.CopyNormalizedResult(destination);

            Assert.That(
                () =>
                {
                    for (var call = 0; call < 10; call++)
                    {
                        _extractor.CopyNormalizedResult(destination);
                    }
                },
                Is.Not.AllocatingGCMemory());
        }

        [UnityTest]
        public IEnumerator SecondRun_OverwritesTheResultWithTheNewGeneration()
        {
            Assert.That(_extractor.Begin(_frame, 1), Is.True);
            yield return RunToCompletion();

            Assert.That(_extractor.Begin(_frame, 2), Is.True);
            yield return RunToCompletion();

            Assert.That(_extractor.Generation, Is.EqualTo(2));
            var embedding = new float[_extractor.EmbeddingDimension];
            Assert.That(_extractor.CopyNormalizedResult(embedding), Is.True);
        }

        [Test]
        public void CopyNormalizedResult_RefusesBeforeARunCompletes()
        {
            var destination = new float[_extractor.EmbeddingDimension];

            Assert.That(_extractor.CopyNormalizedResult(destination), Is.False);

            _extractor.Begin(_frame, 1);
            Assert.That(_extractor.CopyNormalizedResult(destination), Is.False);
        }

        [UnityTest]
        public IEnumerator CopyNormalizedResult_RejectsATooSmallDestination()
        {
            Assert.That(_extractor.Begin(_frame, 1), Is.True);
            yield return RunToCompletion();

            var tooSmall = new float[_extractor.EmbeddingDimension - 1];
            Assert.That(_extractor.CopyNormalizedResult(tooSmall), Is.False);
        }

        [Test]
        public void Cancel_DiscardsTheRunItNames()
        {
            Assert.That(_extractor.Begin(_frame, 7), Is.True);
            _extractor.Step();

            _extractor.Cancel(7);

            Assert.That(_extractor.IsBusy, Is.False);
            Assert.That(_extractor.Step(), Is.EqualTo(EmbeddingStepResult.Idle));
            Assert.That(_extractor.CopyNormalizedResult(new float[_extractor.EmbeddingDimension]), Is.False);
        }

        [Test]
        public void Cancel_LeavesARunFromAnotherGenerationAlone()
        {
            Assert.That(_extractor.Begin(_frame, 7), Is.True);

            _extractor.Cancel(6);

            Assert.That(_extractor.IsBusy, Is.True);
            Assert.That(_extractor.Generation, Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator Cancel_AfterCompletionPublishesNoResult()
        {
            Assert.That(_extractor.Begin(_frame, 3), Is.True);
            yield return RunToCompletion();

            _extractor.Cancel(3);

            Assert.That(_extractor.CopyNormalizedResult(new float[_extractor.EmbeddingDimension]), Is.False);
        }

        [Test]
        public void Dispose_IsIdempotentAndRejectsLaterUse()
        {
            _extractor.Dispose();
            Assert.DoesNotThrow(() => _extractor.Dispose());

            var extractor = _extractor;
            var frame = _frame;
            Assert.Throws<ObjectDisposedException>(() => extractor.Begin(frame, 1));
            Assert.Throws<ObjectDisposedException>(() => extractor.Step());
            Assert.Throws<ObjectDisposedException>(() => extractor.Cancel(1));
        }

        /// <summary>Steps the extractor across frames until the run finishes.</summary>
        private IEnumerator RunToCompletion()
        {
            for (var step = 0; step < MaximumSteps; step++)
            {
                var result = _extractor.Step();
                if (result == EmbeddingStepResult.Completed)
                {
                    yield break;
                }

                Assert.That(
                    result,
                    Is.EqualTo(EmbeddingStepResult.Pending),
                    $"unexpected step result: {result} ({_extractor.FailureMessage})");
                yield return null;
            }

            Assert.Fail($"The run did not finish within {MaximumSteps} steps.");
        }

        /// <summary>A deterministic, non-uniform frame so the embedding is not degenerate.</summary>
        private static Texture2D CreateFrame(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] = new Color32(
                        (byte)((x * 7) % 256),
                        (byte)((y * 11) % 256),
                        (byte)(((x + y) * 13) % 256),
                        255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
