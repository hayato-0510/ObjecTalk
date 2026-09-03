using System;
using ObjecTalk.Domain.Recognition;
using UnityEngine;

namespace ObjecTalk.Core.Recognition
{
    /// <summary>
    /// One capture's local features, held by whoever extracted or restored them.
    /// The concrete implementation owns an OpenCV matrix; this interface deliberately does not
    /// expose it, because the OpenCV+Unity asset ships without an assembly definition and its
    /// types therefore exist only in the predefined <c>Assembly-CSharp</c>.
    /// </summary>
    public interface ILocalFeatureSample : IDisposable
    {
        /// <summary>False when the capture found nothing and the user must retry.</summary>
        bool HasFeatures { get; }

        /// <summary>The payload to persist. Only valid while the sample is undisposed.</summary>
        LocalFeatureBlob Serialize();
    }

    /// <summary>
    /// Bounded local-feature confirmation for the single best neural candidate. Implementations
    /// only ever receive an explicitly prepared CPU-readable image; they never convert a camera
    /// texture themselves.
    /// </summary>
    public interface ILocalFeatureVerifier : IDisposable
    {
        void Initialize();

        /// <summary>
        /// Extracts features from a registration capture. The caller owns the returned sample.
        /// A featureless capture returns a sample reporting <c>HasFeatures == false</c>.
        /// </summary>
        ILocalFeatureSample Extract(Texture2D image);

        /// <summary>
        /// Rebuilds a persisted sample. Returns null when the blob cannot be trusted, so one
        /// corrupt row cannot stop the rest of the database from loading.
        /// </summary>
        ILocalFeatureSample Restore(LocalFeatureBlob blob);

        /// <summary>
        /// True when <paramref name="image"/> shares at least <paramref name="minimumMatches"/>
        /// close descriptors with <paramref name="reference"/>.
        /// </summary>
        bool Verify(Texture2D image, ILocalFeatureSample reference, int minimumMatches);
    }
}
