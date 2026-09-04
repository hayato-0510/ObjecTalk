using Newtonsoft.Json.Linq;

namespace ObjecTalk.Infrastructure.Conversation.OpenAI
{
    public sealed class RealtimeEventParser
    {
        private const string SanitizedApiErrorMessage = "音声会話サービスでエラーが発生しました。";

        public bool TryParse(string json, out RealtimeEvent realtimeEvent)
        {
            realtimeEvent = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            string type = ReadString(payload, "type");
            string responseId = ReadString(payload, "response_id");
            if (string.IsNullOrEmpty(responseId))
            {
                responseId = ReadString(payload["response"] as JObject, "id");
            }

            RealtimeEventKind kind = GetKind(type);
            string transcript = ReadTranscript(payload, kind);
            string errorCode = kind == RealtimeEventKind.ApiError ? RealtimeErrorCodes.ApiError : string.Empty;
            string errorMessage = kind == RealtimeEventKind.ApiError ? SanitizedApiErrorMessage : string.Empty;
            realtimeEvent = new RealtimeEvent(
                kind,
                type,
                ReadString(payload, "event_id"),
                ReadString(payload, "item_id"),
                responseId,
                transcript,
                errorCode,
                errorMessage);
            return true;
        }

        private static string ReadTranscript(JObject payload, RealtimeEventKind kind)
        {
            switch (kind)
            {
                case RealtimeEventKind.UserTranscriptCompleted:
                case RealtimeEventKind.AssistantTranscriptDone:
                    return ReadString(payload, "transcript");
                case RealtimeEventKind.AssistantTranscriptDelta:
                    return ReadString(payload, "delta");
                default:
                    return string.Empty;
            }
        }

        private static RealtimeEventKind GetKind(string type)
        {
            switch (type)
            {
                case RealtimeEventTypes.SpeechStarted:
                    return RealtimeEventKind.SpeechStarted;
                case RealtimeEventTypes.SpeechStopped:
                    return RealtimeEventKind.SpeechStopped;
                case RealtimeEventTypes.UserTranscriptCompleted:
                    return RealtimeEventKind.UserTranscriptCompleted;
                case RealtimeEventTypes.AssistantTranscriptDelta:
                    return RealtimeEventKind.AssistantTranscriptDelta;
                case RealtimeEventTypes.AssistantTranscriptDone:
                    return RealtimeEventKind.AssistantTranscriptDone;
                case RealtimeEventTypes.ResponseStarted:
                    return RealtimeEventKind.ResponseStarted;
                case RealtimeEventTypes.ResponseDone:
                    return RealtimeEventKind.ResponseDone;
                case RealtimeEventTypes.Error:
                    return RealtimeEventKind.ApiError;
                default:
                    return RealtimeEventKind.Unknown;
            }
        }

        private static string ReadString(JObject payload, string propertyName)
        {
            return payload == null ? string.Empty : (string)payload[propertyName] ?? string.Empty;
        }
    }
}
