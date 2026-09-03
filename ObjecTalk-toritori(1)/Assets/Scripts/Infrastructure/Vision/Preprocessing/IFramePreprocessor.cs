using System;
using UnityEngine;

namespace ObjecTalk.Infrastructure.Vision
{
    /// <summary>
    /// The two images one recognition cycle needs from a camera frame. Declared as an interface so
    /// the orchestrator can be exercised without a graphics device, and so ownership of the
    /// reusable surfaces stays with the preprocessor rather than with its caller.
    /// </summary>
    public interface IFramePreprocessor : IDisposable
    {
        /// <summary>Side length of the square handed to the embedding model.</summary>
        int EmbeddingSize { get; }

        /// <summary>
        /// The center-cropped, resized frame for the embedding model. The same instance is
        /// returned on every call, so callers must not retain it beyond the current cycle.
        /// </summary>
        Texture PrepareEmbedding(Texture source);

        /// <summary>
        /// The center-cropped, bounded, CPU-readable frame for local verification. The same
        /// instance is returned on every call while the camera resolution is unchanged.
        /// </summary>
        Texture2D CopyVerificationFrame(Texture source);

        /// <summary>
        /// Releases the reusable surfaces without ending the preprocessor's life, so a paused
        /// application holds no graphics memory and a later call can allocate them again.
        /// </summary>
        void ReleaseBuffers();
    }
}
