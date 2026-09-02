using System.Linq;
using NUnit.Framework;
using ObjecTalk.Domain.Recognition;

namespace ObjecTalk.Tests.EditMode
{
    public class BackendSelectionPolicyTests
    {
        [Test]
        public void SelectInitial_PrefersGpuComputeWhenComputeShadersAreSupported()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);

            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.GpuCompute));
            Assert.That(policy.Current, Is.EqualTo(EmbeddingBackend.GpuCompute));
            Assert.That(policy.IsDisabled, Is.False);
        }

        [Test]
        public void SelectInitial_FallsBackToCpuWhenComputeShadersAreUnsupported()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: false);

            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.Cpu));
        }

        [Test]
        public void SelectInitial_IsIdempotent()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);

            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.GpuCompute));
            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.GpuCompute));
        }

        [Test]
        public void InitializationFailure_AllowsExactlyOneCpuAttemptAfterGpu()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);
            policy.SelectInitial();

            Assert.That(policy.HandleInitializationFailure(), Is.EqualTo(EmbeddingBackend.Cpu));
            Assert.That(policy.Current, Is.EqualTo(EmbeddingBackend.Cpu));
            Assert.That(policy.IsDisabled, Is.False);

            Assert.That(policy.HandleInitializationFailure(), Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.IsDisabled, Is.True);
        }

        [Test]
        public void InitializationFailure_DisablesImmediatelyWhenCpuWasTheOnlyOption()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: false);
            policy.SelectInitial();

            Assert.That(policy.HandleInitializationFailure(), Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.Current, Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.IsDisabled, Is.True);
        }

        [Test]
        public void InitializationFailure_BeforeSelectionDisablesThePolicy()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);

            Assert.That(policy.HandleInitializationFailure(), Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.IsDisabled, Is.True);
        }

        [Test]
        public void DisabledPolicy_StaysDisabledAndRefusesToSelectAgain()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);
            policy.SelectInitial();
            policy.HandleInitializationFailure();
            policy.HandleInitializationFailure();

            Assert.That(policy.HandleInitializationFailure(), Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.IsDisabled, Is.True);
        }

        [Test]
        public void UndisabledPolicy_ReportsNoFailureMessage()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);
            policy.SelectInitial();

            Assert.That(policy.FailureMessage, Is.Empty);
        }

        [Test]
        public void DisabledPolicy_ReportsTheJapaneseRecognitionUnavailableMessage()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: false);
            policy.SelectInitial();
            policy.HandleInitializationFailure();

            Assert.That(policy.FailureMessage, Is.EqualTo(RecognitionMessages.RecognitionUnavailable));
            Assert.That(policy.FailureMessage, Is.Not.Empty);
            Assert.That(
                policy.FailureMessage.Any(IsJapanese),
                Is.True,
                "The user-facing failure message must be Japanese.");
        }

        [Test]
        public void Reset_ClearsTheFailureAndAllowsSelectionAgain()
        {
            var policy = new BackendSelectionPolicy(computeShadersSupported: true);
            policy.SelectInitial();
            policy.HandleInitializationFailure();
            policy.HandleInitializationFailure();

            policy.Reset();

            Assert.That(policy.IsDisabled, Is.False);
            Assert.That(policy.FailureMessage, Is.Empty);
            Assert.That(policy.Current, Is.EqualTo(EmbeddingBackend.None));
            Assert.That(policy.SelectInitial(), Is.EqualTo(EmbeddingBackend.GpuCompute));
        }

        private static bool IsJapanese(char value)
        {
            return (value >= '぀' && value <= 'ヿ')
                || (value >= '一' && value <= '鿿');
        }
    }
}
