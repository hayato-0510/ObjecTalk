using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Core.Recognition
{
    /// <summary>
    /// Stand-in for <c>AkazeVerifier</c> when the OpenCV native library is not in the build — an
    /// ARM64 player without an arm64 <c>OpenCvSharpExtern</c>. It does no local-feature work:
    /// registration captures are accepted, samples round-trip through persistence, and every
    /// candidate that passed neural screening is reported as confirmed. Recognition then rests on
    /// the embedding similarity, the ambiguity margin, and the temporal stabilizer alone.
    ///
    /// Swapping a real <see cref="AkazeVerifier"/> back in (adding the arm64 native library)
    /// needs no code change here; objects registered while this was active must be re-registered,
    /// which the sample-version handling already prompts for.
    /// </summary>
    public sealed class NullLocalFeatureVerifier : ILocalFeatureVerifier
    {
        public void Initialize()
        {
        }

        public ILocalFeatureSample Extract(Texture2D image) => new Sample();

        public ILocalFeatureSample Restore(LocalFeatureBlob blob) => new Sample();

        public LocalVerificationResult Verify(Texture2D image, ILocalFeatureSample reference, int minimumMatches)
            => LocalVerificationResult.Verified();

        public void Dispose()
        {
        }

        private sealed class Sample : ILocalFeatureSample
        {
            // The smallest payload LocalFeatureBlob's guards and the repository accept.
            private static readonly byte[] Placeholder = { 0 };

            public bool HasFeatures => true;

            public LocalFeatureBlob Serialize() =>
                new LocalFeatureBlob(Placeholder, Placeholder, 1, 1, 0, 1, 1);

            public void Dispose()
            {
            }
        }
    }
}
