namespace ObjecTalk.Domain.Recognition
{
    /// <summary>Inference backend chosen for embedding extraction.</summary>
    public enum EmbeddingBackend
    {
        /// <summary>No backend: not selected yet, or recognition is disabled.</summary>
        None = 0,

        /// <summary>GPU compute shaders.</summary>
        GpuCompute = 1,

        /// <summary>CPU fallback.</summary>
        Cpu = 2
    }

    /// <summary>User-facing Japanese messages owned by the recognition domain.</summary>
    public static class RecognitionMessages
    {
        /// <summary>Shown when neither inference backend could be initialized.</summary>
        public const string RecognitionUnavailable = "認識エンジンを初期化できませんでした。物体認識を利用できません。";
    }

    /// <summary>
    /// Chooses the inference backend and governs the single CPU retry.
    /// GPU compute is preferred where it is supported; one initialization failure falls back to
    /// the CPU, and a second failure disables recognition with a message the UI can show as-is.
    /// Pure state, so the policy is testable without a graphics device.
    /// </summary>
    public sealed class BackendSelectionPolicy
    {
        private readonly bool _computeShadersSupported;

        public BackendSelectionPolicy(bool computeShadersSupported)
        {
            _computeShadersSupported = computeShadersSupported;
        }

        /// <summary>Backend currently being attempted, or <see cref="EmbeddingBackend.None"/>.</summary>
        public EmbeddingBackend Current { get; private set; } = EmbeddingBackend.None;

        /// <summary>True once every backend has failed and recognition must stay off.</summary>
        public bool IsDisabled { get; private set; }

        /// <summary>Japanese message to show while disabled; empty otherwise.</summary>
        public string FailureMessage { get; private set; } = string.Empty;

        /// <summary>
        /// Picks the first backend to try. Idempotent, and a no-op once disabled.
        /// </summary>
        public EmbeddingBackend SelectInitial()
        {
            if (IsDisabled)
            {
                return EmbeddingBackend.None;
            }

            if (Current == EmbeddingBackend.None)
            {
                Current = _computeShadersSupported ? EmbeddingBackend.GpuCompute : EmbeddingBackend.Cpu;
            }

            return Current;
        }

        /// <summary>
        /// Records that creating a worker for <see cref="Current"/> failed and returns the next
        /// backend to try, or <see cref="EmbeddingBackend.None"/> when recognition is now disabled.
        /// </summary>
        public EmbeddingBackend HandleInitializationFailure()
        {
            if (IsDisabled)
            {
                return EmbeddingBackend.None;
            }

            if (Current == EmbeddingBackend.GpuCompute)
            {
                Current = EmbeddingBackend.Cpu;
                return Current;
            }

            // Either the single CPU attempt failed, or a failure was reported before any backend
            // was selected. Both leave nothing left to try.
            Current = EmbeddingBackend.None;
            IsDisabled = true;
            FailureMessage = RecognitionMessages.RecognitionUnavailable;
            return EmbeddingBackend.None;
        }

        /// <summary>Clears the disabled state so a later session can attempt initialization again.</summary>
        public void Reset()
        {
            Current = EmbeddingBackend.None;
            IsDisabled = false;
            FailureMessage = string.Empty;
        }
    }
}
