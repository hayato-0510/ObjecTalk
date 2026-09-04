using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ObjecTalk.Infrastructure.Conversation;

namespace ObjecTalk.Tests.EditMode.Conversation
{
    [TestFixture]
    public sealed class FirebaseIdTokenProviderTests
    {
        [Test]
        public async Task GetAsync_AvailableCurrentUser_ForwardsForceRefresh()
        {
            var source = new FakeTokenSource(Task.FromResult("firebase-token"));
            var provider = new FirebaseIdTokenProvider(source);

            string token = await provider.GetAsync(true, CancellationToken.None);

            Assert.That(token, Is.EqualTo("firebase-token"));
            Assert.That(source.ForceRefreshValues, Is.EqualTo(new[] { true }));
        }

        [Test]
        public void GetAsync_SignedOutCurrentUser_ThrowsSanitizedAvailabilityError()
        {
            var provider = new FirebaseIdTokenProvider(new FakeTokenSource(Task.FromResult<string>(null)));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetAsync(false, CancellationToken.None));

            Assert.That(exception.Message, Does.Not.Contain("signed-out diagnostic"));
            Assert.That(exception.Message, Is.Not.Empty);
        }

        [Test]
        public void GetAsync_UnavailableTokenSource_ThrowsSanitizedAvailabilityError()
        {
            var provider = new FirebaseIdTokenProvider(new FakeTokenSource(Task.FromException<string>(new InvalidOperationException("unavailable diagnostic"))));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.GetAsync(false, CancellationToken.None));

            Assert.That(exception.Message, Does.Not.Contain("unavailable diagnostic"));
            Assert.That(exception.Message, Is.Not.Empty);
        }

        [Test, Timeout(1000)]
        public void GetAsync_CallerCancellation_InterruptsUncooperativeTokenSource()
        {
            var neverCompletes = new TaskCompletionSource<string>();
            var provider = new FirebaseIdTokenProvider(new FakeTokenSource(neverCompletes.Task));
            using (var cancellation = new CancellationTokenSource())
            {
                Task<string> request = provider.GetAsync(false, cancellation.Token);
                cancellation.Cancel();

                Assert.ThrowsAsync<OperationCanceledException>(async () => await request);
            }
        }

        private sealed class FakeTokenSource : IFirebaseIdTokenSource
        {
            private readonly Task<string> _token;

            public FakeTokenSource(Task<string> token)
            {
                _token = token;
                ForceRefreshValues = new List<bool>();
            }

            public List<bool> ForceRefreshValues { get; }

            public Task<string> GetCurrentTokenAsync(bool forceRefresh)
            {
                ForceRefreshValues.Add(forceRefresh);
                return _token;
            }
        }
    }
}
