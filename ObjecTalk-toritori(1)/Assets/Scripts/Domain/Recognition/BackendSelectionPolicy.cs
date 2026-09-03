using System;

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

        /// <summary>Shown when the embedding model asset is missing or will not import.</summary>
        public const string ModelUnavailable = "認識モデルを読み込めませんでした。物体認識を利用できません。";

        /// <summary>Shown when the user has refused, or has not yet granted, camera access.</summary>
        public const string CameraPermissionDenied = "カメラの使用が許可されていません。端末の設定から許可してください。";

        /// <summary>Shown when the device reports no usable camera.</summary>
        public const string CameraUnavailable = "カメラを利用できません。";

        /// <summary>Shown while recognition is running but nothing is recognized yet.</summary>
        public const string Searching = "捜索中...";

        /// <summary>Shown when a registration capture found no local features.</summary>
        public const string RegistrationRetry = "特徴を検出できませんでした。角度を変えてもう一度撮影してください。";

        /// <summary>Shown once all required views are captured.</summary>
        public const string RegistrationComplete = "撮影が完了しました。";

        /// <summary>
        /// Shown when persisted features were written by an older model or preprocessing version
        /// and were therefore discarded. Object names, images, and placement survive.
        /// </summary>
        public const string ReRegistrationRequired = "認識データの形式が変わったため、物体の再登録が必要です。";

        /// <summary>Progress while collecting the required views, for example "1/3 枚撮影しました".</summary>
        public static string RegistrationProgress(int capturedViews, int requiredViews)
        {
            if (capturedViews < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capturedViews), "Captured views cannot be negative.");
            }

            if (requiredViews < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredViews), "At least one view is required.");
            }

            return capturedViews >= requiredViews
                ? RegistrationComplete
                : $"{capturedViews}/{requiredViews} 枚撮影しました。角度を変えて撮影してください。";
        }
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
