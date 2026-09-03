using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    /// <summary>
    /// Guards the image-pipeline rule that camera frames are copied explicitly rather than cast.
    /// The scan covers every file in the recognition pipeline; the legacy synchronous matcher it
    /// replaced was removed with the Barracuda dependency.
    /// </summary>
    public class RecognitionSourceScanTests
    {
        private static readonly string[] RecognitionSources =
        {
            "Scripts/Infrastructure/Vision/Preprocessing/CameraFramePreprocessor.cs",
            "Scripts/Infrastructure/Vision/AkazeVerifier.cs",
            "Scripts/Domain/Models/LocalFeatureSample.cs",
            "Scripts/Infrastructure/Vision/LocalFeatureCodec.cs",
            "Scripts/Core/Recognition/ILocalFeatureVerifier.cs",
            "Scripts/Core/Recognition/ObjectRecognitionService.cs"
        };

        [Test]
        public void RecognitionPipeline_NeverConvertsACameraTextureByCasting()
        {
            foreach (var relativePath in RecognitionSources)
            {
                var path = Path.Combine(Application.dataPath, relativePath);
                Assert.That(File.Exists(path), Is.True, $"Missing recognition source {relativePath}");

                var source = File.ReadAllText(path);
                Assert.That(source, Does.Not.Contain("as Texture2D"), relativePath);
                Assert.That(source, Does.Not.Contain("(Texture2D)"), relativePath);
                Assert.That(source, Does.Not.Contain("WebCamTexture"), relativePath);
            }
        }
    }
}
