using NUnit.Framework;
using ObjecTalk.Infrastructure.Conversation.OpenAI;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class RealtimeEventParserTests
    {
        private RealtimeEventParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new RealtimeEventParser();
        }

        [TestCase("{\"type\":\"input_audio_buffer.speech_started\",\"event_id\":\"evt-start\",\"item_id\":\"item-1\"}", RealtimeEventKind.SpeechStarted)]
        [TestCase("{\"type\":\"input_audio_buffer.speech_stopped\",\"event_id\":\"evt-stop\",\"item_id\":\"item-1\"}", RealtimeEventKind.SpeechStopped)]
        [TestCase("{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\"}}", RealtimeEventKind.ResponseStarted)]
        [TestCase("{\"type\":\"response.done\",\"response\":{\"id\":\"resp-1\"}}", RealtimeEventKind.ResponseDone)]
        public void TryParse_OfficialLifecycleEvent_ReturnsExpectedKind(string json, RealtimeEventKind expected)
        {
            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(expected));
        }

        [Test]
        public void TryParse_CompletedUserTranscription_ExtractsItemIdAndTranscript()
        {
            const string json = "{\"type\":\"conversation.item.input_audio_transcription.completed\",\"event_id\":\"evt-1\",\"item_id\":\"item-1\",\"transcript\":\"こんにちは\"}";

            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(RealtimeEventKind.UserTranscriptCompleted));
            Assert.That(realtimeEvent.EventId, Is.EqualTo("evt-1"));
            Assert.That(realtimeEvent.ItemId, Is.EqualTo("item-1"));
            Assert.That(realtimeEvent.Transcript, Is.EqualTo("こんにちは"));
        }

        [TestCase("response.output_audio_transcript.delta", "delta", RealtimeEventKind.AssistantTranscriptDelta)]
        [TestCase("response.output_audio_transcript.done", "transcript", RealtimeEventKind.AssistantTranscriptDone)]
        public void TryParse_AssistantAudioTranscript_ExtractsTranscript(string type, string transcriptProperty, RealtimeEventKind expected)
        {
            string json = "{\"type\":\"" + type + "\",\"response_id\":\"resp-1\",\"item_id\":\"item-1\",\"" + transcriptProperty + "\":\"案内します\"}";

            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(expected));
            Assert.That(realtimeEvent.ResponseId, Is.EqualTo("resp-1"));
            Assert.That(realtimeEvent.ItemId, Is.EqualTo("item-1"));
            Assert.That(realtimeEvent.Transcript, Is.EqualTo("案内します"));
        }

        [Test]
        public void TryParse_ApiError_ReturnsSanitizedTypedError()
        {
            const string upstreamDetail = "upstream diagnostic must not escape";
            string json = "{\"type\":\"error\",\"error\":{\"code\":\"invalid_request_error\",\"message\":\"" + upstreamDetail + "\"}}";

            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(RealtimeEventKind.ApiError));
            Assert.That(realtimeEvent.ErrorCode, Is.EqualTo(RealtimeErrorCodes.ApiError));
            Assert.That(realtimeEvent.ErrorMessage, Does.Not.Contain(upstreamDetail));
            Assert.That(realtimeEvent.ErrorMessage, Is.Not.Empty);
        }

        [Test]
        public void TryParse_UnknownEvent_ReturnsUnknownWithoutRawPayload()
        {
            const string secret = "payload-not-retained";
            string json = "{\"type\":\"future.event\",\"secret\":\"" + secret + "\"}";

            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(RealtimeEventKind.Unknown));
            Assert.That(realtimeEvent.RawType, Is.EqualTo("future.event"));
            Assert.That(realtimeEvent.ToString(), Does.Not.Contain(secret));
        }

        [Test]
        public void TryParse_MissingOptionalFields_ReturnsDeterministicEmptyValues()
        {
            Assert.That(_parser.TryParse("{\"type\":\"response.output_audio_transcript.done\"}", out RealtimeEvent realtimeEvent), Is.True);

            Assert.That(realtimeEvent.Kind, Is.EqualTo(RealtimeEventKind.AssistantTranscriptDone));
            Assert.That(realtimeEvent.EventId, Is.Empty);
            Assert.That(realtimeEvent.ItemId, Is.Empty);
            Assert.That(realtimeEvent.ResponseId, Is.Empty);
            Assert.That(realtimeEvent.Transcript, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{")]
        [TestCase("not json")]
        public void TryParse_MalformedJson_ReturnsFalse(string json)
        {
            Assert.That(_parser.TryParse(json, out RealtimeEvent realtimeEvent), Is.False);
            Assert.That(realtimeEvent, Is.Null);
        }
    }
}
