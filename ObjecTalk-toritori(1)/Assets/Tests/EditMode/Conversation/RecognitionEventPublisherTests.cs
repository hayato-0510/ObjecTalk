using System;
using System.Collections.Generic;
using NUnit.Framework;
using ObjecTalk.Core.Conversation;
using ObjecTalk.Domain.Conversation;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class RecognitionEventPublisherTests
    {
        [Test]
        public void Publish_RepeatedObject_EmitsOneTypedObservationPerCallAndOneLegacyTransition()
        {
            var publisher = new RecognitionEventPublisher();
            var observations = new List<RecognizedObjectState>();
            var legacy = new List<string>();
            publisher.ObservationPublished += observations.Add;
            publisher.LegacyStateChanged += (recognized, name) => legacy.Add($"{recognized}:{name}");
            var firstAt = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.FromHours(9));
            var secondAt = firstAt.AddMilliseconds(100);

            publisher.PublishObservation(true, "object-1", "りんご", 0.91f, firstAt);
            publisher.PublishObservation(true, "object-1", "りんご", 0.94f, secondAt);

            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0].ObjectId, Is.EqualTo("object-1"));
            Assert.That(observations[0].ObjectName, Is.EqualTo("りんご"));
            Assert.That(observations[0].ObservedAt, Is.EqualTo(firstAt.ToUniversalTime()));
            Assert.That(observations[1].Confidence, Is.EqualTo(0.94f));
            Assert.That(legacy, Is.EqualTo(new[] { "True:りんご" }));
        }

        [Test]
        public void Publish_LostOutcome_EmitsOncePerCallAndLegacyOnlyOnTransition()
        {
            var publisher = new RecognitionEventPublisher(true, "本");
            var observations = new List<RecognizedObjectState>();
            var legacy = new List<string>();
            publisher.ObservationPublished += observations.Add;
            publisher.LegacyStateChanged += (recognized, name) => legacy.Add($"{recognized}:{name}");
            var observedAt = DateTimeOffset.UtcNow;

            publisher.PublishObservation(false, "ignored", "ignored", 0.2f, observedAt);
            publisher.PublishObservation(false, "ignored", "ignored", 0.1f, observedAt.AddSeconds(1));

            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0].IsRecognized, Is.False);
            Assert.That(observations[0].ObjectId, Is.Empty);
            Assert.That(observations[0].ObjectName, Is.Empty);
            Assert.That(legacy, Is.EqualTo(new[] { "False:" }));
        }

        [Test]
        public void PublishLegacyState_DoesNotDuplicateTypedTickObservation()
        {
            var publisher = new RecognitionEventPublisher();
            var observations = new List<RecognizedObjectState>();
            publisher.ObservationPublished += observations.Add;
            var observedAt = DateTimeOffset.UtcNow;

            publisher.PublishLegacyState(true, "本");
            publisher.PublishObservation(true, "object-2", "本", 0.9f, observedAt);

            Assert.That(observations, Has.Count.EqualTo(1));
            Assert.That(observations[0].ObjectId, Is.EqualTo("object-2"));
        }
    }
}
