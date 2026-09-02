using System;
using ObjecTalk.Domain.Recognition;
using Unity.InferenceEngine;
using UnityEngine;

namespace ObjecTalk.Infrastructure.AI
{
    /// <summary>Outcome of one slice of inference work.</summary>
    public enum EmbeddingStepResult
    {
        /// <summary>No run is in flight.</summary>
        Idle = 0,

        /// <summary>The run is still executing or waiting for readback.</summary>
        Pending = 1,

        /// <summary>A normalized embedding is ready to copy.</summary>
        Completed = 2,

        /// <summary>
        /// The run failed. The extractor is no longer busy and keeps reporting this until the next
        /// <c>Begin</c> or <c>Cancel</c>, so a caller that steps late still sees the failure.
        /// </summary>
        Failed = 3
    }

    /// <summary>
    /// Graphics capabilities that decide the backend. Injectable so the selection policy can be
    /// exercised without a device.
    /// </summary>
    public interface IBackendCapabilities
    {
        bool SupportsComputeShaders { get; }
    }

    /// <summary>
    /// Runs the embedding model incrementally so a full execution never blocks one frame.
    /// One run is in flight at a time, and each run carries the caller's generation token so a
    /// result produced after cancellation is discarded rather than published.
    /// </summary>
    public interface IEmbeddingExtractor : IDisposable
    {
        /// <summary>Backend in use, or <see cref="EmbeddingBackend.None"/> before initialization.</summary>
        EmbeddingBackend Backend { get; }

        /// <summary>True when no backend could be initialized and recognition must stay off.</summary>
        bool IsDisabled { get; }

        /// <summary>Japanese message for the UI while disabled; empty otherwise.</summary>
        string FailureMessage { get; }

        /// <summary>True while a run is scheduling or awaiting readback.</summary>
        bool IsBusy { get; }

        /// <summary>Generation token of the run in flight, or of the last completed run.</summary>
        int Generation { get; }

        /// <summary>Embedding length the extractor produces; zero before initialization.</summary>
        int EmbeddingDimension { get; }

        /// <summary>
        /// Loads the model, prepends the recorded preprocessing, and creates a worker, falling back
        /// from GPU compute to CPU once. Returns false when the extractor could not be prepared.
        /// </summary>
        bool Initialize(ModelAsset modelAsset, EmbeddingModelSpec spec);

        /// <summary>
        /// Starts a run for <paramref name="input"/>. Returns false when the extractor is busy,
        /// uninitialized, or disabled, so callers never queue overlapping work.
        /// </summary>
        bool Begin(Texture input, int generation);

        /// <summary>Advances the run by a bounded slice of work.</summary>
        EmbeddingStepResult Step();

        /// <summary>
        /// Copies the finished, L2-normalized embedding into <paramref name="destination"/>.
        /// Returns false unless a run has completed and the destination is large enough.
        /// </summary>
        bool CopyNormalizedResult(Span<float> destination);

        /// <summary>
        /// Abandons the run carrying <paramref name="generation"/>. A run from another generation
        /// is left alone, so a late cancel cannot clear a newer run.
        /// </summary>
        void Cancel(int generation);
    }
}
