using System;
using NUnit.Framework;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Tests.EditMode
{
    public class ConversationStateMachineTests
    {
        [Test]
        public void AllowsNormalLifecycle()
        {
            var machine = new ConversationStateMachine();
            var generation = machine.BeginGeneration();

            Assert.That(machine.TryTransition(ConversationState.RequestingPermission, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Connecting, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Listening, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Speaking, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Listening, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Stopping, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Idle, generation), Is.True);
            Assert.That(machine.State, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public void RejectsEventsFromOldGeneration()
        {
            var machine = new ConversationStateMachine();
            var oldGeneration = machine.BeginGeneration();
            var currentGeneration = machine.BeginGeneration();

            Assert.That(currentGeneration, Is.GreaterThan(oldGeneration));
            Assert.That(machine.TryTransition(ConversationState.Connecting, oldGeneration), Is.False);
            Assert.That(machine.State, Is.EqualTo(ConversationState.Idle));
        }

        [Test]
        public void RejectsInvalidTransitionsWithoutChangingState()
        {
            var machine = new ConversationStateMachine();
            var generation = machine.BeginGeneration();

            Assert.That(machine.TryTransition(ConversationState.Listening, generation), Is.False);
            Assert.That(machine.State, Is.EqualTo(ConversationState.Idle));
            Assert.That(machine.TryTransition(ConversationState.RequestingPermission, generation), Is.True);
            Assert.That(machine.TryTransition(ConversationState.Speaking, generation), Is.False);
            Assert.That(machine.State, Is.EqualTo(ConversationState.RequestingPermission));
        }

        [Test]
        public void ForceIdleIsIdempotentForActiveGenerationAndIgnoresStaleGeneration()
        {
            var machine = new ConversationStateMachine();
            var firstGeneration = machine.BeginGeneration();
            Assert.That(machine.TryTransition(ConversationState.RequestingPermission, firstGeneration), Is.True);
            Assert.That(machine.ForceIdle(firstGeneration), Is.True);
            Assert.That(machine.ForceIdle(firstGeneration), Is.True);
            Assert.That(machine.State, Is.EqualTo(ConversationState.Idle));

            var activeGeneration = machine.BeginGeneration();
            Assert.That(machine.TryTransition(ConversationState.RequestingPermission, activeGeneration), Is.True);
            Assert.That(machine.ForceIdle(firstGeneration), Is.False);
            Assert.That(machine.State, Is.EqualTo(ConversationState.RequestingPermission));
        }

        [Test]
        public void ContextNormalizesOptionalValuesAndRejectsMissingRequiredValues()
        {
            var context = new ConversationContext("object-1", "りんご", null, "user-1", "  ");

            Assert.That(context.PersonalityPrompt, Is.EqualTo(string.Empty));
            Assert.That(context.Language, Is.EqualTo("ja"));
            Assert.That(() => new ConversationContext(null, "りんご", "", "user-1", "ja"), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new ConversationContext("object-1", null, "", "user-1", "ja"), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new ConversationContext("object-1", "りんご", "", null, "ja"), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
