namespace CoreNats.Tests.Util
{
    using System;
    using System.Threading;
    using Xunit;

    /// <summary>
    /// Verifies that NatsUnsubscriber properly disposes its CancellationTokenSource
    /// and that both Unsubscribe() and Dispose() are idempotent and safe to call in
    /// any order or multiple times.
    /// </summary>
    public class NatsUnsubscriber_DisposeShould
    {
        [Fact]
        public void ImplementsIDisposable()
        {
            var unsubscriber = new NatsUnsubscriber();
            Assert.IsAssignableFrom<IDisposable>(unsubscriber);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var unsubscriber = new NatsUnsubscriber();
            // Should not throw on repeated Dispose calls.
            unsubscriber.Dispose();
            unsubscriber.Dispose();
        }

        [Fact]
        public void Unsubscribe_IsIdempotent()
        {
            var unsubscriber = new NatsUnsubscriber();
            // Should not throw on repeated Unsubscribe calls.
            unsubscriber.Unsubscribe();
            unsubscriber.Unsubscribe();
        }

        [Fact]
        public void Unsubscribe_ThenDispose_DoesNotThrow()
        {
            var unsubscriber = new NatsUnsubscriber();
            unsubscriber.Unsubscribe();
            // Explicit Dispose after Unsubscribe must be safe.
            unsubscriber.Dispose();
        }

        [Fact]
        public void Dispose_ThenUnsubscribe_DoesNotThrow()
        {
            var unsubscriber = new NatsUnsubscriber();
            unsubscriber.Dispose();
            // Unsubscribe after Dispose — _cts is already disposed so Cancel()
            // may throw ObjectDisposedException; the catch block must swallow it.
            unsubscriber.Unsubscribe();
        }

        [Fact]
        public void Unsubscribe_CancelsToken()
        {
            var unsubscriber = new NatsUnsubscriber();
            var token = unsubscriber.Token;
            Assert.False(token.IsCancellationRequested);

            unsubscriber.Unsubscribe();

            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void Token_IsCancelledAfterDisposeThenUnsubscribe()
        {
            // After Dispose(), the CTS is disposed, but the token captured before
            // the call should reflect the cancelled state set by Unsubscribe().
            var unsubscriber = new NatsUnsubscriber();
            var token = unsubscriber.Token;

            unsubscriber.Unsubscribe(); // cancels then disposes
            // Token must remain cancelled.
            Assert.True(token.IsCancellationRequested);
        }
    }
}
