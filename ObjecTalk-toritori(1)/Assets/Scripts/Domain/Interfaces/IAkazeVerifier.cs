using ObjecTalk.Domain.Models;
using UnityEngine;

namespace ObjecTalk.Domain.Interfaces
{
    /// <summary>
    /// Bounded local-feature confirmation for the single best neural candidate.
    /// Implementations only ever receive an explicitly prepared CPU-readable image; they never
    /// convert a camera texture themselves.
    /// </summary>
    public interface IAkazeVerifier : System.IDisposable
    {
        void Initialize();

        /// <summary>
        /// Extracts keypoints and descriptors for a registration capture. The caller owns the
        /// returned sample and must dispose it. A featureless capture returns a sample whose
        /// <see cref="LocalFeatureSample.HasFeatures"/> is false so the flow can request a retry.
        /// </summary>
        LocalFeatureSample Extract(Texture2D image);

        /// <summary>
        /// True when <paramref name="image"/> shares at least <paramref name="minimumMatches"/>
        /// close descriptors with <paramref name="reference"/>.
        /// </summary>
        bool Verify(Texture2D image, LocalFeatureSample reference, int minimumMatches);
    }
}
