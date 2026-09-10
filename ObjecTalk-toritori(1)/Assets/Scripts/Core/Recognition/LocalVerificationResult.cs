namespace ObjecTalk.Core.Recognition
{
    /// <summary>
    /// What one local-feature confirmation found: whether the candidate is confirmed, and where in
    /// the frame the matching keypoints clustered. The location is a by-product of the matching the
    /// verifier already does, so reporting it costs nothing extra, but it is optional - a
    /// textureless object can be confirmed on too few points to place reliably.
    /// </summary>
    public readonly struct LocalVerificationResult
    {
        private LocalVerificationResult(bool isVerified, bool hasLocation, float relativeX, float relativeY)
        {
            IsVerified = isVerified;
            HasLocation = hasLocation;
            RelativeX = relativeX;
            RelativeY = relativeY;
        }

        public static LocalVerificationResult Rejected => default;

        public static LocalVerificationResult Verified()
            => new LocalVerificationResult(true, false, 0f, 0f);

        /// <summary>Confirmed, with the matched cluster at a 0..1 bottom-left-origin position.</summary>
        public static LocalVerificationResult VerifiedAt(float relativeX, float relativeY)
            => new LocalVerificationResult(true, true, relativeX, relativeY);

        public bool IsVerified { get; }

        /// <summary>False when the match was too sparse to locate; the caller keeps its last placement.</summary>
        public bool HasLocation { get; }

        public float RelativeX { get; }

        public float RelativeY { get; }
    }
}
