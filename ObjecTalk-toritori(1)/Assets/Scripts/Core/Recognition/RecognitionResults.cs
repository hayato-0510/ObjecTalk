using System;

namespace ObjecTalk.Core.Recognition
{
    /// <summary>What one call to <see cref="ObjectRecognitionService.Tick"/> accomplished.</summary>
    public enum RecognitionTickStatus
    {
        /// <summary>Nothing is in flight.</summary>
        Idle = 0,

        /// <summary>The run in flight advanced but has not finished.</summary>
        Pending = 1,

        /// <summary>A recognition run finished and the stabilized state names an object.</summary>
        Recognized = 2,

        /// <summary>A recognition run finished and no object is stably recognized.</summary>
        NotRecognized = 3,

        /// <summary>A registration capture finished and was kept.</summary>
        CaptureAccepted = 4,

        /// <summary>Inference failed. The run was abandoned and the service is idle again.</summary>
        Failed = 5
    }

    public readonly struct RecognitionTickResult
    {
        internal RecognitionTickResult(
            RecognitionTickStatus status,
            string objectId,
            float score,
            int capturedViewCount)
            : this(status, objectId, score, capturedViewCount, false, 0f, 0f)
        {
        }

        internal RecognitionTickResult(
            RecognitionTickStatus status,
            string objectId,
            float score,
            int capturedViewCount,
            bool hasLocation,
            float relativeX,
            float relativeY)
        {
            Status = status;
            ObjectId = objectId;
            Score = score;
            CapturedViewCount = capturedViewCount;
            HasLocation = hasLocation;
            RelativeX = relativeX;
            RelativeY = relativeY;
        }

        public RecognitionTickStatus Status { get; }

        /// <summary>The stabilized object, or null unless <see cref="Status"/> is Recognized.</summary>
        public string ObjectId { get; }

        /// <summary>Aggregated cosine score of the recognized object; zero otherwise.</summary>
        public float Score { get; }

        /// <summary>Registration captures kept so far.</summary>
        public int CapturedViewCount { get; }

        /// <summary>
        /// True when this tick located the recognized object in the frame. False on every other
        /// status, and on a recognized tick whose match was too sparse to place.
        /// </summary>
        public bool HasLocation { get; }

        /// <summary>Object position across the frame, 0..1 from the left. Zero without a location.</summary>
        public float RelativeX { get; }

        /// <summary>Object position up the frame, 0..1 from the bottom. Zero without a location.</summary>
        public float RelativeY { get; }
    }

    /// <summary>
    /// A change in the stabilized recognition state. Only published when the state actually
    /// changes, never once per inference cycle.
    /// </summary>
    public readonly struct RecognitionStateChange
    {
        internal RecognitionStateChange(string objectId, float score)
        {
            ObjectId = objectId;
            Score = score;
        }

        /// <summary>The newly recognized object, or null when recognition was lost.</summary>
        public string ObjectId { get; }

        public float Score { get; }

        public bool IsRecognized => !string.IsNullOrEmpty(ObjectId);
    }

    /// <summary>Why a registration capture was refused, so the UI can ask for the right retry.</summary>
    public enum RegistrationCaptureStatus
    {
        /// <summary>Inference started; the result arrives from <see cref="ObjectRecognitionService.Tick"/>.</summary>
        Started = 0,

        /// <summary>No registration is open, or three captures already exist.</summary>
        NotAccepting = 1,

        /// <summary>The frame carried no local features and the user must retry.</summary>
        Featureless = 2,

        /// <summary>The extractor was busy or unavailable.</summary>
        Unavailable = 3
    }
}
