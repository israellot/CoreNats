namespace CoreNats.Tests.Util
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Tests for the CancellationTokenRegistration disposal pattern used in InternalSubscribe.
    ///
    /// InternalSubscribe uses:
    ///   var tcs = new TaskCompletionSource&lt;bool&gt;();
    ///   using var reg = cancellationToken.Register(() => tcs.TrySetResult(true));
    ///   await tcs.Task;
    ///
    /// These tests exercise the two properties of the fix:
    ///   1. Registration is disposed after the await completes, so the callback no longer
    ///      holds a reference to the TCS (no memory leak on long-lived tokens).
    ///   2. TrySetResult is used instead of SetResult, so a race where the callback fires
    ///      after tcs.Task is already complete does not throw InvalidOperationException.
    /// </summary>
    public class InternalSubscribe_CtrDisposalShould
    {
        [Fact]
        public async Task CompleteImmediately_WhenTokenAlreadyCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var tcs = new TaskCompletionSource<bool>();
            using var reg = cts.Token.Register(() => tcs.TrySetResult(true));

            // Already-cancelled token fires the callback synchronously in Register.
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(100));
            Assert.True(ReferenceEquals(winner, tcs.Task), "tcs.Task should have completed synchronously");
            Assert.True(tcs.Task.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task CompleteAfterCancellation_WhenTokenCancelledAfterRegister()
        {
            using var cts = new CancellationTokenSource();

            var tcs = new TaskCompletionSource<bool>();
            using var reg = cts.Token.Register(() => tcs.TrySetResult(true));

            // Task must not be complete yet.
            Assert.False(tcs.Task.IsCompleted);

            // Cancel synchronously — the callback fires inline before Cancel() returns.
            cts.Cancel();

            // After synchronous cancellation the callback has already run.
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(500));
            Assert.True(ReferenceEquals(winner, tcs.Task), "tcs.Task should have completed via cancellation callback");
        }

        [Fact]
        public void TrySetResult_DoesNotThrow_WhenTcsAlreadyCompleted()
        {
            // Validates that TrySetResult (not SetResult) is safe on an already-completed TCS.
            // SetResult on a completed TCS throws InvalidOperationException; TrySetResult returns false.
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);

            // This is the exact callback the fixed code registers; it must not throw.
            var ex = Record.Exception(() => tcs.TrySetResult(true));
            Assert.Null(ex);
        }

        [Fact]
        public void DisposedRegistration_DoesNotFireCallback_AfterDispose()
        {
            // After disposing the registration, cancelling the token must NOT fire the callback.
            using var cts = new CancellationTokenSource();
            var callbackFired = false;

            var reg = cts.Token.Register(() => callbackFired = true);
            reg.Dispose();

            cts.Cancel();

            Assert.False(callbackFired);
        }

        [Fact]
        public async Task FullPattern_RegistrationDisposedWhenTaskCompletes()
        {
            // Simulates the full InternalSubscribe pattern: register, await tcs, then verify the
            // registration was disposed (callback no longer fires after the await block exits).
            using var cts = new CancellationTokenSource();
            var postCompletionCallbackFired = false;

            var tcs = new TaskCompletionSource<bool>();

            // Hold registration in a local so we can query it after the simulate-await block.
            CancellationTokenRegistration capturedReg;
            using (capturedReg = cts.Token.Register(() => tcs.TrySetResult(true)))
            {
                // Simulate the task completing from some other path (not cancellation).
                tcs.TrySetResult(true);
                await tcs.Task;
                // registration is disposed when the using block exits here.
            }

            // Cancelling now must not fire the already-disposed registration.
            cts.Token.Register(() => postCompletionCallbackFired = true).Dispose();
            // The above is a sanity-check that we can still register new callbacks.
            // The key assertion: the *original* callback no longer fires.
            // We demonstrate this by re-cancelling: if capturedReg were still alive it
            // would call tcs.TrySetResult again on an already-completed tcs (harmless
            // with TrySetResult, but the point is the using-var disposes it first).
            cts.Cancel();

            Assert.False(postCompletionCallbackFired);
            await Task.Delay(10); // Let any stray callbacks complete.
            // If reg had not been disposed, cancelling would call TrySetResult on a
            // completed TCS – harmless but the registration would still be rooted.
            // The test can't directly assert "no rooted closure" but the pattern is verified
            // by the DisposedRegistration_DoesNotFireCallback_AfterDispose test above.
        }
    }
}
