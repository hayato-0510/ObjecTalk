using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ObjecTalk.Tests.EditMode
{
    /// <summary>
    /// Guards the image-pipeline rule that camera frames are copied explicitly rather than cast.
    /// The scan covers the new recognition pipeline; the legacy Barracuda matcher it replaces is
    /// still referenced by the old controller and is removed in a later task.
    /// </summary>
    public class RecognitionSourceScanTests
    {
        private static readonly string[] RecognitionSources =
        {
            "Scripts/Infrastructure/Vision/Preprocessing/CameraFramePreprocessor.cs",
            "Scripts/Infrastructure/Vision/AkazeVerifier.cs",
            "Scripts/Domain/Models/LocalFeatureSample.cs",
            "Scripts/Domain/Interfaces/IAkazeVerifier.cs"
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
