using System;
using NUnit.Framework;
using ObjecTalk.Core.Exhibition;

namespace ObjecTalk.Tests.EditMode.Exhibition
{
    [TestFixture]
    public sealed class ExhibitionIdlePolicyTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan IdleToAttract = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan AttractToReset = TimeSpan.FromSeconds(30);

        private static ExhibitionIdlePolicy CreatePolicy() => new ExhibitionIdlePolicy(IdleToAttract, AttractToReset);

        [Test]
        public void Constructor_RejectsNonPositiveSpans()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExhibitionIdlePolicy(TimeSpan.Zero, AttractToReset));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExhibitionIdlePolicy(IdleToAttract, TimeSpan.FromSeconds(-1)));
        }

        [Test]
        public void FreshPolicy_ShowsAttract_AndNeverResetsWithoutActivity()
        {
            var policy = CreatePolicy();

            Assert.That(policy.Evaluate(T0), Is.EqualTo(ExhibitionIdleState.Attract));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromHours(3)), Is.EqualTo(ExhibitionIdleState.Attract));
        }

        [Test]
        public void RightAfterActivity_IsInteracting()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0), Is.EqualTo(ExhibitionIdleState.Interacting));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(44)), Is.EqualTo(ExhibitionIdleState.Interacting));
        }

        [Test]
        public void IdlePastAttractThreshold_ShowsAttract()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(45)), Is.EqualTo(ExhibitionIdleState.Attract));
            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(74)), Is.EqualTo(ExhibitionIdleState.Attract));
        }

        [Test]
        public void IdlePastResetThreshold_RequestsReset()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromSeconds(75)), Is.EqualTo(ExhibitionIdleState.Reset));
        }

        [Test]
        public void ActiveConversation_SuppressesAttractAndReset()
        {
            var policy = CreatePolicy();
            policy.RecordActivity(T0);
            policy.SetConversationActive(true, T0);

            Assert.That(policy.Evaluate(T0 + TimeSpan.FromMinutes(10)), Is.EqualTo(ExhibitionIdleState.Interacting));

            policy.SetConversationActive(false, T0 + TimeSpan.FromMinutes(10));
            Assert.That(
                policy.Evaluate(T0 + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(75)),
                Is.EqualTo(ExhibitionIdleState.Reset));
        }
    }
}
