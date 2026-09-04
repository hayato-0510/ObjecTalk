using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Contract of the exported embedding model: tensor names and shapes, the preprocessing
    /// constants recorded with the export, and the version strings persisted beside each sample.
    /// Engine-free so the contract can be asserted without Unity.
    /// </summary>
    [Serializable]
    public readonly struct EmbeddingModelSpec
    {
        /// <summary>Channels the model consumes; the preprocessing arrays must match.</summary>
        public const int ChannelCount = 3;

        private const string DefaultModelVersion = "mobilenetv3-small-imagenet-fp16-v1";
        private const string DefaultPreprocessingVersion = "rgb01-imagenet-mean-std-crop75-upright-v2";
        private const string DefaultInputName = "image";
        private const string DefaultOutputName = "embedding";
        private const int DefaultEmbeddingDimension = 576;

        private readonly float[] _mean;
        private readonly float[] _standardDeviation;

        public EmbeddingModelSpec(
            string modelVersion,
            string preprocessingVersion,
            string inputName,
            string outputName,
            int inputSize,
            int embeddingDimension,
            float[] channelMeans,
            float[] channelStandardDeviations)
        {
            RequireName(modelVersion, nameof(modelVersion));
            RequireName(preprocessingVersion, nameof(preprocessingVersion));
            RequireName(inputName, nameof(inputName));
            RequireName(outputName, nameof(outputName));

            if (inputSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputSize), "The model input size must be positive.");
            }

            if (embeddingDimension <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(embeddingDimension),
                    "The embedding dimension must be positive.");
            }

            ModelVersion = modelVersion;
            PreprocessingVersion = preprocessingVersion;
            InputName = inputName;
            OutputName = outputName;
            InputSize = inputSize;
            EmbeddingDimension = embeddingDimension;
            _mean = CopyMean(channelMeans, nameof(channelMeans));
            _standardDeviation = CopyStandardDeviation(channelStandardDeviations, nameof(channelStandardDeviations));
        }

        /// <summary>Contract of the model committed under <c>Assets/Models</c>.</summary>
        public static EmbeddingModelSpec Default => new EmbeddingModelSpec(
            DefaultModelVersion,
            DefaultPreprocessingVersion,
            DefaultInputName,
            DefaultOutputName,
            ImageGeometry.EmbeddingImageSize,
            DefaultEmbeddingDimension,
            new[] { 0.485f, 0.456f, 0.406f },
            new[] { 0.229f, 0.224f, 0.225f });

        public string ModelVersion { get; }

        public string PreprocessingVersion { get; }

        public string InputName { get; }

        public string OutputName { get; }

        public int InputSize { get; }

        public int EmbeddingDimension { get; }

        public float MeanForChannel(int channel)
        {
            RequireChannel(channel);
            return _mean[channel];
        }

        public float StandardDeviationForChannel(int channel)
        {
            RequireChannel(channel);
            return _standardDeviation[channel];
        }

        private static void RequireChannel(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    $"The model has {ChannelCount} channels.");
            }
        }

        private static void RequireName(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("The model contract requires a non-empty name.", paramName);
            }
        }

        private static float[] CopyMean(float[] values, string paramName)
        {
            var copy = CopyChannels(values, paramName);
            foreach (var value in copy)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentException("Channel means must be finite.", paramName);
                }
            }

            return copy;
        }

        private static float[] CopyStandardDeviation(float[] values, string paramName)
        {
            var copy = CopyChannels(values, paramName);
            foreach (var value in copy)
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                {
                    throw new ArgumentException(
                        "Channel standard deviations must be finite and greater than zero.",
                        paramName);
                }
            }

            return copy;
        }

        private static float[] CopyChannels(float[] values, string paramName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (values.Length != ChannelCount)
            {
                throw new ArgumentException(
                    $"Preprocessing constants require exactly {ChannelCount} channels.",
                    paramName);
            }

            return (float[])values.Clone();
        }
    }
}
