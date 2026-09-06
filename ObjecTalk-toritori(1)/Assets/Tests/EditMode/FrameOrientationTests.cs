using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class FrameOrientationTests
    {
        [TestCase(0, 0)]
        [TestCase(90, 90)]
        [TestCase(180, 180)]
        [TestCase(270, 270)]
        [TestCase(-90, 270)]
        [TestCase(450, 90)]
        [TestCase(360, 0)]
        public void FromWebCam_NormalizesTheAngle(int input, int expected)
        {
            var orientation = FrameOrientation.FromWebCam(input, false);
            Assert.That(orientation.RotationDegrees, Is.EqualTo(expected));
        }

        [Test]
        public void FromWebCam_CarriesTheMirrorFlag()
        {
            Assert.That(FrameOrientation.FromWebCam(0, true).Mirrored, Is.True);
        }

        [Test]
        public void OrientedSize_SwapsWidthAndHeightForQuarterTurns()
        {
            var ninety = FrameOrientation.FromWebCam(90, false);
            Assert.That(ninety.OrientedSize(1280, 720), Is.EqualTo((720, 1280)));

            var oneEighty = FrameOrientation.FromWebCam(180, false);
            Assert.That(oneEighty.OrientedSize(1280, 720), Is.EqualTo((1280, 720)));
        }

        [Test]
        public void Identity_LeavesCornerUVsAtTheUnitSquare()
        {
            var uvs = FrameOrientation.FromWebCam(0, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo((0f, 0f)));
            Assert.That(uvs[1], Is.EqualTo((1f, 0f)));
            Assert.That(uvs[2], Is.EqualTo((1f, 1f)));
            Assert.That(uvs[3], Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Rotate90_RotatesTheContentClockwiseToUpright()
        {
            // WebCamTexture.videoRotationAngle is the clockwise angle the raw frame must be
            // rotated by to look upright (matches Webcamera.cs: localEulerAngles.z = -angle).
            // For 90 degrees, the oriented bottom-left corner samples the source's bottom-right.
            var uvs = FrameOrientation.FromWebCam(90, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo((1f, 0f)));  // oriented BL  <- source BR
            Assert.That(uvs[1], Is.EqualTo((1f, 1f)));  // oriented BR  <- source TR
            Assert.That(uvs[2], Is.EqualTo((0f, 1f)));  // oriented TR  <- source TL
            Assert.That(uvs[3], Is.EqualTo((0f, 0f)));  // oriented TL  <- source BL
        }

        [Test]
        public void Rotate180_TurnsTheContentHalfway()
        {
            var uvs = FrameOrientation.FromWebCam(180, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo((1f, 1f)));  // oriented BL  <- source TR
            Assert.That(uvs[1], Is.EqualTo((0f, 1f)));  // oriented BR  <- source TL
            Assert.That(uvs[2], Is.EqualTo((0f, 0f)));  // oriented TR  <- source BL
            Assert.That(uvs[3], Is.EqualTo((1f, 0f)));  // oriented TL  <- source BR
        }

        [Test]
        public void Rotate270_RotatesTheContentThreeQuarterTurnsClockwise()
        {
            var uvs = FrameOrientation.FromWebCam(270, false).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo((0f, 1f)));  // oriented BL  <- source TL
            Assert.That(uvs[1], Is.EqualTo((0f, 0f)));  // oriented BR  <- source BL
            Assert.That(uvs[2], Is.EqualTo((1f, 0f)));  // oriented TR  <- source BR
            Assert.That(uvs[3], Is.EqualTo((1f, 1f)));  // oriented TL  <- source TR
        }

        [Test]
        public void Mirrored_FlipsHorizontallyInSourceSpaceBeforeRotation()
        {
            var plain = FrameOrientation.FromWebCam(0, false).SourceUVsForOrientedCorners();
            var mirrored = FrameOrientation.FromWebCam(0, true).SourceUVsForOrientedCorners();

            Assert.That(mirrored[0].u, Is.EqualTo(1f - plain[0].u));
            Assert.That(mirrored[1].u, Is.EqualTo(1f - plain[1].u));
        }

        [Test]
        public void MirroredWithRotation_MirrorsSourceUThenRotates()
        {
            // Mirror is applied to the source UVs first, then the rotation shift, matching
            // Webcamera.cs where localScale.x = -1 composes before localEulerAngles.
            var uvs = FrameOrientation.FromWebCam(90, true).SourceUVsForOrientedCorners();

            Assert.That(uvs[0], Is.EqualTo((0f, 0f)));  // (1,0) mirrored to (0,0), then rotate
            Assert.That(uvs[1], Is.EqualTo((0f, 1f)));
            Assert.That(uvs[2], Is.EqualTo((1f, 1f)));
            Assert.That(uvs[3], Is.EqualTo((1f, 0f)));
        }
    }
}
