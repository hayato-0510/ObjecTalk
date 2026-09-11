using System;

namespace ObjecTalk.Domain.Recognition
{
    /// <summary>
    /// Engine-free maths for making the face overlay follow its object. Registration crops to the
    /// centre of the frame, so the face placed at registration time is expressed relative to a
    /// centred object; tracking is therefore a translation by however far the object has since
    /// drifted from that centre.
    /// </summary>
    public static class FaceOverlayTracking
    {
        /// <summary>Where the object sits in a registration capture, by construction.</summary>
        public const float RegistrationCentre = 0.5f;

        /// <summary>
        /// Moves one placed piece (an eye or a mouth) by the object's offset from the centre.
        /// Both pieces take the same offset, so their spacing is preserved.
        /// </summary>
        public static (float x, float y) TrackedPosition(
            float registeredRelativeX,
            float registeredRelativeY,
            float trackedRelativeX,
            float trackedRelativeY)
        {
            return (
                registeredRelativeX + (trackedRelativeX - RegistrationCentre),
                registeredRelativeY + (trackedRelativeY - RegistrationCentre));
        }

        /// <summary>
        /// Exponential approach to <paramref name="target"/>. Recognition only produces a new
        /// position two to five times a second, so the overlay is eased toward it every frame
        /// instead of stepping. The exponential form makes the result frame-rate independent.
        /// A non-positive time constant snaps.
        /// </summary>
        public static float Smooth(float current, float target, float deltaTime, float timeConstantSeconds)
        {
            if (timeConstantSeconds <= 0f)
            {
                return target;
            }

            if (deltaTime <= 0f)
            {
                return current;
            }

            var t = 1f - (float)Math.Exp(-deltaTime / timeConstantSeconds);
            return current + ((target - current) * t);
        }
    }
}
