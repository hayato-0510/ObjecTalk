using System;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class LocalFeatureMatchPolicyTests
    {
        [Test]
        public void CountGoodMatches_CountsOnlyDistancesBelowTheMaximum()
        {
            var distances = new[] { 0f, 10f, 49.9f, 50f, 50.1f, 200f };

            Assert.That(LocalFeatureMatchPolicy.CountGoodMatches(distances, 50f), Is.EqualTo(3));
        }

        [Test]
        public void CountGoodMatches_IgnoresNonFiniteAndNegativeDistances()
        {
            var distances = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 5f };

            Assert.That(LocalFeatureMatchPolicy.CountGoodMatches(distances, 50f), Is.EqualTo(1));
        }

        [Test]
        public void CountGoodMatches_ReturnsZeroForAnEmptySet()
        {
            Assert.That(LocalFeatureMatchPolicy.CountGoodMatches(Array.Empty<float>(), 50f), Is.EqualTo(0));
        }

        [Test]
        public void CountGoodMatches_RejectsNonPositiveOrNonFiniteMaximumDistance()
        {
            var distances = new[] { 1f };

            Assert.Throws<ArgumentOutOfRangeException>(() => LocalFeatureMatchPolicy.CountGoodMatches(distances, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => LocalFeatureMatchPolicy.CountGoodMatches(distances, -5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => LocalFeatureMatchPolicy.CountGoodMatches(distances, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LocalFeatureMatchPolicy.CountGoodMatches(distances, float.PositiveInfinity));
        }

        [Test]
        public void IsVerified_RequiresAtLeastTheMinimumNumberOfGoodMatches()
        {
            var distances = new[] { 1f, 2f, 3f, 4f, 90f };

            Assert.That(LocalFeatureMatchPolicy.IsVerified(distances, 50f, 4), Is.True);
            Assert.That(LocalFeatureMatchPolicy.IsVerified(distances, 50f, 5), Is.False);
        }

        [Test]
        public void IsVerified_RejectsAnEmptySetEvenWhenOneMatchIsRequired()
        {
            Assert.That(LocalFeatureMatchPolicy.IsVerified(Array.Empty<float>(), 50f, 1), Is.False);
        }

        [Test]
        public void IsVerified_RejectsANonPositiveMinimumMatchCount()
        {
            var distances = new[] { 1f };

            Assert.Throws<ArgumentOutOfRangeException>(() => LocalFeatureMatchPolicy.IsVerified(distances, 50f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LocalFeatureMatchPolicy.IsVerified(distances, 50f, -3));
        }

        [Test]
        public void DefaultMaximumDescriptorDistance_MatchesTheHammingBudgetForAkaze()
        {
            Assert.That(LocalFeatureMatchPolicy.DefaultMaximumDescriptorDistance, Is.EqualTo(50f).Within(1e-6f));
        }
    }
}
