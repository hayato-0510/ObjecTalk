using System;
using System.Collections;
using ObjecTalk.Domain.Recognition;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

// Sentis declares its own Debug type, so the engine logger has to be named explicitly.
using Debug = UnityEngine.Debug;

namespace ObjecTalk.Infrastructure.AI
{
    /// <summary>Reads the real device capabilities.</summary>
    public sealed class SystemBackendCapabilities : IBackendCapabilities
    {
        public bool SupportsComputeShaders => SystemInfo.supportsComputeShaders;
    }

    /// <summary>
    /// Sentis-backed embedding extractor. The model is scheduled a bounded number of layers per
    /// <see cref="Step"/> call and the output is read back asynchronously, so no single frame pays
    /// for a whole execution. The input tensor and the result array are allocated once and reused.
    /// </summary>
    public sealed class SentisEmbeddingExtractor : IEmbeddingExtractor
    {
        /// <summary>Layers scheduled per <see cref="Step"/> call.</summary>
        public const int DefaultLayersPerStep = 20;

        private enum RunState
        {
            Idle,
            Scheduling,
            AwaitingReadback,
            Completed,
            Failed
        }

        private readonly int _layersPerStep;
        private readonly BackendSelectionPolicy _policy;

        private EmbeddingModelSpec _spec;
        private Worker _worker;
        private Tensor<float> _inputTensor;
        private Tensor<float> _outputTensor;
        private TextureTransform _inputTransform;
        private IEnumerator _schedule;
        private float[] _result;
        private RunState _state = RunState.Idle;
        private bool _initialized;
        private bool _disposed;

        public SentisEmbeddingExtractor()
            : this(DefaultLayersPerStep, new SystemBackendCapabilities())
        {
        }

        public SentisEmbeddingExtractor(int layersPerStep, IBackendCapabilities capabilities)
        {
            if (layersPerStep < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layersPerStep),
                    "At least one layer must be scheduled per step.");
            }

            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            _layersPerStep = layersPerStep;
            _policy = new BackendSelectionPolicy(capabilities.SupportsComputeShaders);
        }

        public EmbeddingBackend Backend => _policy.Current;

        public bool IsDisabled => _policy.IsDisabled;

        public string FailureMessage { get; private set; } = string.Empty;

        public bool IsBusy => _state == RunState.Scheduling || _state == RunState.AwaitingReadback;

        public int Generation { get; private set; }

        public int EmbeddingDimension => _initialized ? _spec.EmbeddingDimension : 0;

        public bool Initialize(ModelAsset modelAsset, EmbeddingModelSpec spec)
        {
            ThrowIfDisposed();
            if (modelAsset == null)
            {
                throw new ArgumentNullException(nameof(modelAsset));
            }

            if (_initialized)
            {
                return true;
            }

            Model runtimeModel;
            try
            {
                var source = ModelLoader.Load(modelAsset);
                if (!TryValidateContract(source, spec, out var contractFailure))
                {
                    Fail(contractFailure);
                    return false;
                }

                runtimeModel = BuildNormalizedModel(source, spec);
            }
            catch (Exception exception)
            {
                Fail($"Failed to load the embedding model: {exception.Message}");
                return false;
            }

            for (var backend = _policy.SelectInitial();
                 backend != EmbeddingBackend.None;
                 backend = _policy.HandleInitializationFailure())
            {
                try
                {
                    _worker = new Worker(runtimeModel, ToBackendType(backend));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"{nameof(SentisEmbeddingExtractor)}: {backend} initialization failed. {exception.Message}");
                    continue;
                }

                _spec = spec;
                _inputTensor = new Tensor<float>(new TensorShape(1, EmbeddingModelSpec.ChannelCount, spec.InputSize, spec.InputSize));
                // Dimensions come from the target tensor; SetDimensions is deprecated.
                _inputTransform = new TextureTransform()
                    .SetTensorLayout(TensorLayout.NCHW)
                    .SetCoordOrigin(CoordOrigin.TopLeft);
                _result = new float[spec.EmbeddingDimension];
                _initialized = true;
                FailureMessage = string.Empty;

                // Logged once at initialization, never per frame.
                Debug.Log($"{nameof(SentisEmbeddingExtractor)}: using the {backend} backend.");
                return true;
            }

            FailureMessage = _policy.FailureMessage;
            return false;
        }

        public bool Begin(Texture input, int generation)
        {
            ThrowIfDisposed();
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (!_initialized || _policy.IsDisabled || IsBusy)
            {
                return false;
            }

            try
            {
                TextureConverter.ToTensor(input, _inputTensor, _inputTransform);
                _schedule = _worker.ScheduleIterable(_inputTensor);
            }
            catch (Exception exception)
            {
                FailRun($"Failed to schedule inference: {exception.Message}");
                return false;
            }

            Generation = generation;
            _outputTensor = null;
            _state = RunState.Scheduling;
            return true;
        }

        public EmbeddingStepResult Step()
        {
            ThrowIfDisposed();

            try
            {
                switch (_state)
                {
                    case RunState.Scheduling:
                        return StepScheduling();
                    case RunState.AwaitingReadback:
                        return StepReadback();
                    case RunState.Completed:
                        return EmbeddingStepResult.Completed;
                    case RunState.Failed:
                        return EmbeddingStepResult.Failed;
                    default:
                        return EmbeddingStepResult.Idle;
                }
            }
            catch (Exception exception)
            {
                FailRun($"Inference failed: {exception.Message}");
                return EmbeddingStepResult.Failed;
            }
        }

        public bool CopyNormalizedResult(Span<float> destination)
        {
            ThrowIfDisposed();
            if (_state != RunState.Completed || destination.Length < _result.Length)
            {
                return false;
            }

            new ReadOnlySpan<float>(_result).CopyTo(destination);
            return true;
        }

        public void Cancel(int generation)
        {
            ThrowIfDisposed();
            if (_state == RunState.Idle || generation != Generation)
            {
                return;
            }

            ResetRun();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _schedule = null;
            _outputTensor = null;
            _inputTensor?.Dispose();
            _inputTensor = null;
            _worker?.Dispose();
            _worker = null;
            _result = null;
            _initialized = false;
        }

        private EmbeddingStepResult StepScheduling()
        {
            for (var scheduled = 0; scheduled < _layersPerStep; scheduled++)
            {
                if (_schedule.MoveNext())
                {
                    continue;
                }

                _outputTensor = _worker.PeekOutput(_spec.OutputName) as Tensor<float>;
                if (_outputTensor == null)
                {
                    FailRun($"The model produced no float output named '{_spec.OutputName}'.");
                    return EmbeddingStepResult.Failed;
                }

                _outputTensor.ReadbackRequest();
                _state = RunState.AwaitingReadback;
                return EmbeddingStepResult.Pending;
            }

            return EmbeddingStepResult.Pending;
        }

        private EmbeddingStepResult StepReadback()
        {
            if (!_outputTensor.IsReadbackRequestDone())
            {
                return EmbeddingStepResult.Pending;
            }

            if (_outputTensor.shape.length != _result.Length)
            {
                FailRun($"Expected a {_result.Length}-value embedding, got {_outputTensor.shape.length}.");
                return EmbeddingStepResult.Failed;
            }

            // Reading dataOnBackend keeps the tensor on its backend and, after a completed readback
            // request, returns the already-downloaded data without blocking or allocating.
            NativeArray<float> downloaded = _outputTensor.dataOnBackend.Download<float>(_result.Length);
            NativeArray<float>.Copy(downloaded, 0, _result, 0, _result.Length);

            try
            {
                EmbeddingMath.NormalizeInPlace(_result);
            }
            catch (ArgumentException exception)
            {
                FailRun($"The model produced an unusable embedding: {exception.Message}");
                return EmbeddingStepResult.Failed;
            }

            _schedule = null;
            _outputTensor = null;
            _state = RunState.Completed;
            return EmbeddingStepResult.Completed;
        }

        /// <summary>
        /// Wraps the exported model so the recorded mean and standard deviation are applied on the
        /// inference device. The exported ONNX takes already-normalized values, and the camera
        /// preprocessor hands over plain RGB in [0, 1], so the gap is closed here rather than by a
        /// per-frame CPU pass over 150k floats.
        /// </summary>
        private static Model BuildNormalizedModel(Model source, EmbeddingModelSpec spec)
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(
                new TensorShape(1, EmbeddingModelSpec.ChannelCount, spec.InputSize, spec.InputSize),
                spec.InputName);

            var channelShape = new TensorShape(1, EmbeddingModelSpec.ChannelCount, 1, 1);
            var mean = Functional.Constant(channelShape, ChannelConstants(spec, isMean: true));
            var standardDeviation = Functional.Constant(channelShape, ChannelConstants(spec, isMean: false));

            var outputs = Functional.Forward(source, (input - mean) / standardDeviation);
            graph.AddOutput(outputs[0], spec.OutputName);
            return graph.Compile();
        }

        private static float[] ChannelConstants(EmbeddingModelSpec spec, bool isMean)
        {
            var values = new float[EmbeddingModelSpec.ChannelCount];
            for (var channel = 0; channel < values.Length; channel++)
            {
                values[channel] = isMean
                    ? spec.MeanForChannel(channel)
                    : spec.StandardDeviationForChannel(channel);
            }

            return values;
        }

        private static bool TryValidateContract(Model model, EmbeddingModelSpec spec, out string failure)
        {
            failure = null;

            if (model.inputs.Count != 1 || !string.Equals(model.inputs[0].name, spec.InputName, StringComparison.Ordinal))
            {
                failure = $"The model must have exactly one input named '{spec.InputName}'.";
                return false;
            }

            var shape = model.inputs[0].shape;
            if (!shape.IsStatic())
            {
                failure = "The model input shape must be static.";
                return false;
            }

            var dimensions = shape.ToIntArray();
            if (dimensions.Length != 4
                || dimensions[0] != 1
                || dimensions[1] != EmbeddingModelSpec.ChannelCount
                || dimensions[2] != spec.InputSize
                || dimensions[3] != spec.InputSize)
            {
                failure = $"The model input must be [1,{EmbeddingModelSpec.ChannelCount},{spec.InputSize},{spec.InputSize}].";
                return false;
            }

            if (model.outputs.Count != 1
                || !string.Equals(model.outputs[0].name, spec.OutputName, StringComparison.Ordinal))
            {
                failure = $"The model must have exactly one output named '{spec.OutputName}'.";
                return false;
            }

            return true;
        }

        private static BackendType ToBackendType(EmbeddingBackend backend)
        {
            switch (backend)
            {
                case EmbeddingBackend.GpuCompute:
                    return BackendType.GPUCompute;
                case EmbeddingBackend.Cpu:
                    return BackendType.CPU;
                default:
                    throw new ArgumentOutOfRangeException(nameof(backend), backend, "No worker exists for this backend.");
            }
        }

        private void ResetRun()
        {
            _schedule = null;
            _outputTensor = null;
            _state = RunState.Idle;
        }

        private void FailRun(string message)
        {
            FailureMessage = message;
            Debug.LogError($"{nameof(SentisEmbeddingExtractor)}: {message}");
            _schedule = null;
            _outputTensor = null;
            _state = RunState.Failed;
        }

        private void Fail(string message)
        {
            FailureMessage = message;
            Debug.LogError($"{nameof(SentisEmbeddingExtractor)}: {message}");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SentisEmbeddingExtractor));
            }
        }
    }
}
