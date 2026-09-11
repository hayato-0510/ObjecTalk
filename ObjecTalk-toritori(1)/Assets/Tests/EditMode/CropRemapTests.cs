using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class CropRemapTests
    {
        // A portrait registration snapshot at the default crop: a 540x540 square taken from
        // (90, 370) of a 720x1280 frame.
        private static NormalizedRect PortraitCrop()
            => ImageGeometry.NormalizedCenterCrop(720, 1280, ImageGeometry.DefaultCropFraction);

        [Test]
        public void ToCropSpace_LeavesTheCentreAtTheCentre()
        {
            var (x, y) = CropRemap.ToCropSpace(0.5f, 0.5f, PortraitCrop());

            Assert.That(x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void ToCropSpace_StretchesAPositionToTheCropsOwnCoordinates()
        {
            // 0.25 across a 720-wide frame is pixel 180; the crop starts at 90 and is 540 wide,
            // so that lands one sixth of the way in.
            var (x, _) = CropRemap.ToCropSpace(0.25f, 0.5f, PortraitCrop());

            Assert.That(x, Is.EqualTo((180f - 90f) / 540f).Within(1e-4f));
        }

        [Test]
        public void ToCropSpace_StretchesTheShortAxisMoreThanTheLongOne()
        {
            // The same relative offset means far more pixels vertically on a portrait frame, so
            // the vertical remap has to move further. Getting this wrong is the whole bug: the
            // naming preview shows the crop, the placement screen showed the full frame.
            var crop = PortraitCrop();

            var (x, y) = CropRemap.ToCropSpace(0.6f, 0.6f, crop);

            Assert.That(y - 0.5f, Is.GreaterThan(x - 0.5f));
        }

        [Test]
        public void ToCropSpace_ClampsAPositionThatFallsOutsideTheCrop()
        {
            // A face placed on the very edge of the full frame is not inside the square that was
            // actually registered; pin it to the border rather than drawing it off the photo.
            var (x, y) = CropRemap.ToCropSpace(0f, 0f, PortraitCrop());

            Assert.That(x, Is.EqualTo(0f));
            Assert.That(y, Is.EqualTo(0f));
        }

        [Test]
        public void ToCropSpace_IsIdentityForACropThatTakesEverything()
        {
            var whole = ImageGeometry.NormalizedCenterCrop(400, 400, 1f);

            var (x, y) = CropRemap.ToCropSpace(0.3f, 0.8f, whole);

            Assert.That(x, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(0.8f).Within(1e-5f));
        }
    }
}
