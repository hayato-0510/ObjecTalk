using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Unity.InferenceEngine;

namespace ObjecTalk.Tests.EditMode
{
    public class EmbeddingModelContractTests
    {
        private const string AssetPath = "Assets/Models/mobilenetv3-small-embedding-fp16.onnx";
        private const string ModelVersion = "mobilenetv3-small-imagenet-fp16-v1";
        private const int EmbeddingDimension = 576;

        [Test]
        public void Model_ImportsWithFixedEmbeddingContractAndProducesUnitVector()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(AssetPath);
            Assert.That(modelAsset, Is.Not.Null, $"Missing model asset at {AssetPath}");

            var importer = AssetImporter.GetAtPath(AssetPath);
            Assert.That(importer.userData, Is.EqualTo(ModelVersion));

            var model = ModelLoader.Load(modelAsset);
            Assert.That(model.inputs, Has.Count.EqualTo(1));
            Assert.That(model.inputs[0].name, Is.EqualTo("image"));
            Assert.That(model.inputs[0].shape.IsStatic(), Is.True);
            Assert.That(model.inputs[0].shape.ToIntArray(), Is.EqualTo(new[] { 1, 3, 224, 224 }));
            Assert.That(model.outputs, Has.Count.EqualTo(1));
            Assert.That(model.outputs[0].name, Is.EqualTo("embedding"));

            var rgb = Enumerable.Repeat(0.5f, 3 * 224 * 224).ToArray();
            var mean = new[] { 0.485f, 0.456f, 0.406f };
            var standardDeviation = new[] { 0.229f, 0.224f, 0.225f };
            for (var channel = 0; channel < 3; channel++)
            {
                var offset = channel * 224 * 224;
                for (var pixel = 0; pixel < 224 * 224; pixel++)
                {
                    rgb[offset + pixel] = (rgb[offset + pixel] - mean[channel]) / standardDeviation[channel];
                }
            }

            using var input = new Tensor<float>(new TensorShape(1, 3, 224, 224), rgb);
            using var worker = new Worker(model, BackendType.CPU);
            worker.Schedule(input);
            using var output = worker.PeekOutput("embedding").ReadbackAndClone() as Tensor<float>;

            Assert.That(output, Is.Not.Null, "Expected a float embedding tensor");
            Assert.That(output.shape, Is.EqualTo(new TensorShape(1, EmbeddingDimension)));
            var values = output.DownloadToArray();
            Assert.That(values, Has.Length.EqualTo(EmbeddingDimension));
            Assert.That(values.All(float.IsFinite), Is.True);
            var squaredNorm = values.Sum(value => (double)value * value);
            Assert.That(Math.Sqrt(squaredNorm), Is.EqualTo(1.0).Within(1e-4));
        }
    }
}
