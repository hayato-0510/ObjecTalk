using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class FaceOverlayTrackingTests
    {
        [Test]
        public void TrackedPosition_LeavesTheFaceAloneWhenTheObjectIsCentred()
        {
            // Registration crops to the centre of the frame, so a centred object means the live
            // view matches the registration view and the placed face must not move at all.
            var (x, y) = FaceOverlayTracking.TrackedPosition(
                registeredRelativeX: 0.5f, registeredRelativeY: 0.54f,
                trackedRelativeX: 0.5f, trackedRelativeY: 0.5f);

            Assert.That(x, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0.54f).Within(1e-6f));
        }

        [Test]
        public void TrackedPosition_ShiftsTheFaceByTheObjectsOffsetFromCentre()
        {
            var (x, y) = FaceOverlayTracking.TrackedPosition(
                registeredRelativeX: 0.5f, registeredRelativeY: 0.54f,
                trackedRelativeX: 0.7f, trackedRelativeY: 0.3f);

            Assert.That(x, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0.34f).Within(1e-6f));
        }

        [Test]
        public void TrackedPosition_KeepsTheGapBetweenTheEyesAndTheMouth()
        {
            // Both pieces take the same offset, so their spacing has to survive the shift.
            var (_, eyeY) = FaceOverlayTracking.TrackedPosition(0.5f, 0.54f, 0.2f, 0.9f);
            var (_, mouthY) = FaceOverlayTracking.TrackedPosition(0.5f, 0.46f, 0.2f, 0.9f);

            Assert.That(eyeY - mouthY, Is.EqualTo(0.08f).Within(1e-6f));
        }

        [Test]
        public void Smooth_MovesPartwayTowardTheTargetInOneTimeConstant()
        {
            var smoothed = FaceOverlayTracking.Smooth(
                current: 0f, target: 1f, deltaTime: 0.2f, timeConstantSeconds: 0.2f);

            Assert.That(smoothed, Is.EqualTo(1f - (float)Math.Exp(-1.0)).Within(1e-5f));
        }

        [Test]
        public void Smooth_LandsInTheSamePlaceWhateverTheFrameRate()
        {
            // The whole point of the exponential form: two half steps must equal one full step,
            // so the face does not chase faster on a 60fps device than on a 30fps one.
            var oneStep = FaceOverlayTracking.Smooth(0f, 1f, deltaTime: 0.1f, timeConstantSeconds: 0.2f);

            var half = FaceOverlayTracking.Smooth(0f, 1f, deltaTime: 0.05f, timeConstantSeconds: 0.2f);
            var twoSteps = FaceOverlayTracking.Smooth(half, 1f, deltaTime: 0.05f, timeConstantSeconds: 0.2f);

            Assert.That(twoSteps, Is.EqualTo(oneStep).Within(1e-5f));
        }

        [Test]
        public void Smooth_HoldsStillWhenNoTimeHasPassed()
        {
            var smoothed = FaceOverlayTracking.Smooth(0.25f, 0.75f, deltaTime: 0f, timeConstantSeconds: 0.2f);

            Assert.That(smoothed, Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void Smooth_SnapsToTheTargetWithoutATimeConstant()
        {
            var smoothed = FaceOverlayTracking.Smooth(0.25f, 0.75f, deltaTime: 0.016f, timeConstantSeconds: 0f);

            Assert.That(smoothed, Is.EqualTo(0.75f).Within(1e-6f));
        }
    }
}
