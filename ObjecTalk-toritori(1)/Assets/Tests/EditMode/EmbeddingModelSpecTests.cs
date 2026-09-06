using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class EmbeddingModelSpecTests
    {
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] StandardDeviation = { 0.229f, 0.224f, 0.225f };

        [Test]
        public void Default_MatchesTheExportedModelContract()
        {
            var spec = EmbeddingModelSpec.Default;

            Assert.That(spec.ModelVersion, Is.EqualTo("mobilenetv3-small-imagenet-fp16-v1"));
            Assert.That(spec.InputName, Is.EqualTo("image"));
            Assert.That(spec.OutputName, Is.EqualTo("embedding"));
            Assert.That(spec.InputSize, Is.EqualTo(ImageGeometry.EmbeddingImageSize));
            Assert.That(spec.EmbeddingDimension, Is.EqualTo(576));
        }

        [Test]
        public void Default_CarriesTheImageNetPreprocessingConstants()
        {
            var spec = EmbeddingModelSpec.Default;

            for (var channel = 0; channel < 3; channel++)
            {
                Assert.That(spec.MeanForChannel(channel), Is.EqualTo(Mean[channel]).Within(1e-6f));
                Assert.That(
                    spec.StandardDeviationForChannel(channel),
                    Is.EqualTo(StandardDeviation[channel]).Within(1e-6f));
            }
        }

        [Test]
        public void Default_HasANonEmptyPreprocessingVersion()
        {
            Assert.That(EmbeddingModelSpec.Default.PreprocessingVersion, Is.Not.Empty);
        }

        [Test]
        public void Default_UsesTheCrop75UprightPreprocessingVersion()
        {
            Assert.That(
                EmbeddingModelSpec.Default.PreprocessingVersion,
                Is.EqualTo("rgb01-imagenet-mean-std-crop75-upright-v2"));
        }

        [Test]
        public void Constructor_RejectsMissingNames()
        {
            Assert.Throws<ArgumentException>(() => Create(modelVersion: " "));
            Assert.Throws<ArgumentException>(() => Create(preprocessingVersion: ""));
            Assert.Throws<ArgumentException>(() => Create(inputName: null));
            Assert.Throws<ArgumentException>(() => Create(outputName: "  "));
        }

        [Test]
        public void Constructor_RejectsNonPositiveDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(inputSize: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(inputSize: -224));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(embeddingDimension: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(embeddingDimension: -1));
        }

        [Test]
        public void Constructor_RequiresThreeChannelsOfPreprocessing()
        {
            Assert.Throws<ArgumentException>(() => Create(mean: new[] { 0.5f, 0.5f }));
            Assert.Throws<ArgumentException>(() => Create(standardDeviation: new[] { 0.2f, 0.2f, 0.2f, 0.2f }));

            // Passed directly so the helper's defaulting cannot mask a null argument.
            Assert.Throws<ArgumentNullException>(() => new EmbeddingModelSpec(
                "m", "p", "image", "embedding", 224, 576, null, (float[])StandardDeviation.Clone()));
            Assert.Throws<ArgumentNullException>(() => new EmbeddingModelSpec(
                "m", "p", "image", "embedding", 224, 576, (float[])Mean.Clone(), null));
        }

        [Test]
        public void Constructor_RejectsNonFiniteOrDegeneratePreprocessing()
        {
            Assert.Throws<ArgumentException>(() => Create(mean: new[] { float.NaN, 0.5f, 0.5f }));
            Assert.Throws<ArgumentException>(() => Create(mean: new[] { float.PositiveInfinity, 0.5f, 0.5f }));
            Assert.Throws<ArgumentException>(() => Create(standardDeviation: new[] { 0f, 0.2f, 0.2f }));
            Assert.Throws<ArgumentException>(() => Create(standardDeviation: new[] { -0.2f, 0.2f, 0.2f }));
            Assert.Throws<ArgumentException>(() => Create(standardDeviation: new[] { float.NaN, 0.2f, 0.2f }));
        }

        [Test]
        public void Constructor_CopiesThePreprocessingArrays()
        {
            var mean = new[] { 0.1f, 0.2f, 0.3f };
            var spec = Create(mean: mean);

            mean[0] = 99f;

            Assert.That(spec.MeanForChannel(0), Is.EqualTo(0.1f).Within(1e-6f));
        }

        [Test]
        public void ChannelAccessors_RejectOutOfRangeChannels()
        {
            var spec = EmbeddingModelSpec.Default;

            Assert.Throws<ArgumentOutOfRangeException>(() => spec.MeanForChannel(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => spec.MeanForChannel(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => spec.StandardDeviationForChannel(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => spec.StandardDeviationForChannel(3));
        }

        private static EmbeddingModelSpec Create(
            string modelVersion = "test-model-v1",
            string preprocessingVersion = "test-preprocessing-v1",
            string inputName = "image",
            string outputName = "embedding",
            int inputSize = 224,
            int embeddingDimension = 576,
            float[] mean = null,
            float[] standardDeviation = null)
        {
            return new EmbeddingModelSpec(
                modelVersion,
                preprocessingVersion,
                inputName,
                outputName,
                inputSize,
                embeddingDimension,
                mean ?? (float[])Mean.Clone(),
                standardDeviation ?? (float[])StandardDeviation.Clone());
        }
    }
}
