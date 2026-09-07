using System;

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    public static class RealtimeEventTypes
    {
        public const string SpeechStarted = "input_audio_buffer.speech_started";
        public const string SpeechStopped = "input_audio_buffer.speech_stopped";
        public const string UserTranscriptCompleted = "conversation.item.input_audio_transcription.completed";
        public const string AssistantTranscriptDelta = "response.output_audio_transcript.delta";
        public const string AssistantTranscriptDone = "response.output_audio_transcript.done";
        public const string ResponseStarted = "response.created";
        public const string ResponseDone = "response.done";
        // WebRTC-only playback events: the assistant's audio actually leaving / done leaving the
        // speaker. They lag response.created/response.done by the size of the playback buffer, so
        // they are the accurate cue for muting/unmuting the microphone against echo.
        public const string OutputAudioStarted = "output_audio_buffer.started";
        public const string OutputAudioStopped = "output_audio_buffer.stopped";
        public const string Error = "error";
    }

    public static class RealtimeErrorCodes
    {
        public const string ApiError = "realtime_api_error";
    }

    public enum RealtimeEventKind
    {
        Unknown,
        SpeechStarted,
        SpeechStopped,
        UserTranscriptCompleted,
        AssistantTranscriptDelta,
        AssistantTranscriptDone,
        ResponseStarted,
        ResponseDone,
        OutputAudioStarted,
        OutputAudioStopped,
        ApiError
    }

    public sealed class RealtimeEvent
    {
        public RealtimeEvent(
            RealtimeEventKind kind,
            string rawType,
            string eventId,
            string itemId,
            string responseId,
            string transcript,
            string errorCode,
            string errorMessage)
        {
            Kind = kind;
            RawType = rawType ?? string.Empty;
            EventId = eventId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            ResponseId = responseId ?? string.Empty;
            Transcript = transcript ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public RealtimeEventKind Kind { get; }

        public string RawType { get; }

        public string EventId { get; }

        public string ItemId { get; }

        public string ResponseId { get; }

        public string Transcript { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }

        public override string ToString()
        {
            return $"RealtimeEvent({Kind}, eventId={EventId})";
        }
    }
}
