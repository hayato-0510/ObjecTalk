using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Tests.EditMode
{
    public class ObjectConversationCoordinatorTests
    {
        private FakeClock clock;
        private FakeRepository repository;
        private FakeVoiceConversationService voice;
        private ObjectConversationCoordinator coordinator;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
            repository = new FakeRepository();
            repository.Contexts["apple"] = Context("apple");
            repository.Contexts["book"] = Context("book");
            repository.Contexts["cup"] = Context("cup");
            voice = new FakeVoiceConversationService();
            coordinator = new ObjectConversationCoordinator(clock, repository, voice);
        }

        [Test]
        public async Task StartsImmediatelyForTheFirstRecognizedObjectWithContext()
        {
            await coordinator.ObserveAsync(Seen("apple"));

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
            Assert.That(voice.StopCalls, Is.Empty);
        }

        [Test]
        public async Task KeepsConversationBeforeLossGraceAndStopsAtExactTenSecondBoundary()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            clock.Advance(TimeSpan.FromSeconds(9.9));
            await coordinator.ObserveAsync(Lost());
            await coordinator.TickAsync();

            Assert.That(voice.StopCalls, Is.Empty);

            clock.Advance(TimeSpan.FromSeconds(0.1));
            await coordinator.TickAsync();

            Assert.That(voice.StopCalls, Is.EqualTo(new[] { ConversationStopReason.ObjectLost }));
        }

        [Test]
        public async Task ReacquiringTheActiveObjectRetainsTheExistingSession()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            clock.Advance(TimeSpan.FromSeconds(9));
            await coordinator.ObserveAsync(Lost());
            await coordinator.ObserveAsync(Seen("apple"));
            clock.Advance(TimeSpan.FromSeconds(9));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
            Assert.That(voice.StopCalls, Is.Empty);
        }

        [Test]
        public async Task SwitchesOnlyWhenTheSameCandidateReachesTheExactConfirmationBoundary()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            await coordinator.ObserveAsync(Seen("book"));
            clock.Advance(TimeSpan.FromSeconds(1.9));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));

            clock.Advance(TimeSpan.FromSeconds(0.1));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple", "book" }));
            Assert.That(voice.StopCalls, Is.EqualTo(new[] { ConversationStopReason.ObjectChanged }));
            Assert.That(voice.OperationLog, Is.EqualTo(new[] { "start:apple", "stop:ObjectChanged", "start:book" }));
        }

        [Test]
        public async Task ChangingCandidatesRestartsTheConfirmationInterval()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            await coordinator.ObserveAsync(Seen("book"));
            clock.Advance(TimeSpan.FromSeconds(1.5));
            await coordinator.ObserveAsync(Seen("cup"));
            clock.Advance(TimeSpan.FromSeconds(0.5));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));

            clock.Advance(TimeSpan.FromSeconds(1.5));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple", "cup" }));
        }

        [Test]
        public async Task CandidateDisappearanceRestartsTheConfirmationInterval()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            await coordinator.ObserveAsync(Seen("book"));
            clock.Advance(TimeSpan.FromSeconds(1));
            await coordinator.ObserveAsync(Lost());
            clock.Advance(TimeSpan.FromSeconds(1));
            await coordinator.ObserveAsync(Seen("book"));
            clock.Advance(TimeSpan.FromSeconds(1));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));

            clock.Advance(TimeSpan.FromSeconds(1));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple", "book" }));
        }

        [Test]
        public async Task LossWithoutAnActiveSessionIsANoOp()
        {
            await coordinator.ObserveAsync(Lost());
            clock.Advance(TimeSpan.FromSeconds(20));
            await coordinator.TickAsync();

            Assert.That(voice.StartedObjectIds, Is.Empty);
            Assert.That(voice.StopCalls, Is.Empty);
        }

        [Test]
        public async Task MissingContextDoesNotStartOrPreventALaterValidStart()
        {
            await coordinator.ObserveAsync(Seen("missing"));
            await coordinator.ObserveAsync(Seen("apple"));

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
            Assert.That(voice.StopCalls, Is.Empty);
        }

        [Test]
        public async Task RepositoryFailureRaisesTypedFailureAndLaterObservationRecovers()
        {
            var failures = new List<ConversationFailure>();
            coordinator.FailureOccurred += failures.Add;
            repository.ExceptionToThrow = new InvalidOperationException("database unavailable");

            await coordinator.ObserveAsync(Seen("apple"));

            repository.ExceptionToThrow = null;
            await coordinator.ObserveAsync(Seen("apple"));

            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures[0].Code, Is.EqualTo("conversation_context_lookup_failed"));
            Assert.That(failures[0].UserMessage, Is.Not.Empty);
            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
        }

        [Test]
        public async Task ServiceStartFailureRaisesTypedFailureAndLaterObservationRecovers()
        {
            var failures = new List<ConversationFailure>();
            coordinator.FailureOccurred += failures.Add;
            voice.StartException = new InvalidOperationException("transport unavailable");

            await coordinator.ObserveAsync(Seen("apple"));

            voice.StartException = null;
            await coordinator.ObserveAsync(Seen("apple"));

            Assert.That(failures.Select(failure => failure.Code), Is.EqualTo(new[] { "conversation_start_failed" }));
            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
        }

        [Test]
        public async Task FailedStopDoesNotClaimTheActiveSessionWasCleared()
        {
            await coordinator.ObserveAsync(Seen("apple"));
            voice.StopException = new InvalidOperationException("transport unavailable");

            await coordinator.StopAsync(ConversationStopReason.UserRequested);
            await coordinator.ObserveAsync(Seen("apple"));

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
            Assert.That(voice.StopCalls, Is.EqualTo(new[] { ConversationStopReason.UserRequested }));
        }

        [Test]
        public async Task ConcurrentObservationsDoNotDuplicateTheInitialStart()
        {
            voice.YieldDuringStart = true;

            await Task.WhenAll(
                coordinator.ObserveAsync(Seen("apple")),
                coordinator.ObserveAsync(Seen("apple")));

            Assert.That(voice.StartedObjectIds, Is.EqualTo(new[] { "apple" }));
        }

        private RecognizedObjectState Seen(string objectId)
        {
            return new RecognizedObjectState(objectId, objectId, true, 0.9f, clock.UtcNow);
        }

        private RecognizedObjectState Lost()
        {
            return new RecognizedObjectState(null, null, false, 0f, clock.UtcNow);
        }

        private static ConversationContext Context(string objectId)
        {
            return new ConversationContext(objectId, objectId, string.Empty, "user-1", "ja");
        }

        private sealed class FakeClock : IClock
        {
            public FakeClock(DateTimeOffset utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTimeOffset UtcNow { get; private set; }

            public void Advance(TimeSpan duration)
            {
                UtcNow = UtcNow.Add(duration);
            }
        }

        private sealed class FakeRepository : IConversationObjectRepository
        {
            public Dictionary<string, ConversationContext> Contexts { get; } = new Dictionary<string, ConversationContext>();

            public Exception ExceptionToThrow { get; set; }

            public ConversationContext GetContext(string objectId)
            {
                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                ConversationContext context;
                return Contexts.TryGetValue(objectId, out context) ? context : null;
            }
        }

        private sealed class FakeVoiceConversationService : IVoiceConversationService
        {
            public event Action<ConversationState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action<string> UserTranscriptReceived
            {
                add { }
                remove { }
            }

            public event Action<string> AssistantTranscriptReceived
            {
                add { }
                remove { }
            }

            public event Action<ConversationFailure> ErrorOccurred
            {
                add { }
                remove { }
            }

            public List<string> StartedObjectIds { get; } = new List<string>();
            public List<ConversationStopReason> StopCalls { get; } = new List<ConversationStopReason>();
            public List<string> OperationLog { get; } = new List<string>();
            public Exception StartException { get; set; }
            public Exception StopException { get; set; }
            public bool YieldDuringStart { get; set; }
            public ConversationState CurrentState { get; private set; }

            public void SetPushToTalkActive(bool active) => OperationLog.Add($"push-to-talk:{active}");

            public async Task StartAsync(ConversationContext context, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (YieldDuringStart)
                {
                    await Task.Yield();
                }

                if (StartException != null)
                {
                    throw StartException;
                }

                StartedObjectIds.Add(context.ObjectId);
                OperationLog.Add("start:" + context.ObjectId);
            }

            public Task StopAsync(ConversationStopReason reason, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StopCalls.Add(reason);
                OperationLog.Add("stop:" + reason);
                if (StopException != null)
                {
                    throw StopException;
                }

                return Task.CompletedTask;
            }
        }
    }
}
