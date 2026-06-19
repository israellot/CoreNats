namespace CoreNats.Tests
{
    using System;
    using System.Threading.Tasks;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Verifies that the fire-and-forget Subscribe() overloads observe task faults via
    /// ObserveSubscribeTask rather than silently swallowing them.
    ///
    /// The real early-fault path for InternalSubscribe is when the sender channel's
    /// Write throws before the lifetime-TCS await (e.g. ChannelClosedException if the
    /// writer were ever completed, or OperationCanceledException on a pre-cancelled token).
    ///
    /// Because the fault is delivered asynchronously via a continuation, we test
    /// ObserveSubscribeTask directly (it is internal; InternalsVisibleTo("CoreNats.Tests")
    /// is set in Assembly.cs) by passing it a synthetically faulted ValueTask, confirming
    /// the fault is routed to ConnectionException and not re-thrown or lost.
    /// </summary>
    public class NatsConnection_SubscribeObserveFaultsShould
    {
        [Fact]
        public async Task FaultedTask_RaisesConnectionException()
        {
            // Arrange
            var connection = new NatsConnection(new NatsDefaultOptions());

            Exception? observed = null;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            connection.ConnectionException += (_, ex) =>
            {
                observed = ex;
                tcs.TrySetResult(true);
            };

            var expected = new InvalidOperationException("simulated channel fault");

            // Act — pass a pre-faulted task; the continuation must fire and route to ConnectionException
            var faultedTask = new ValueTask(Task.FromException(expected));
            connection.ObserveSubscribeTask(faultedTask);

            // Assert — continuation runs synchronously (ExecuteSynchronously flag), but give it a moment
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.True(completed == tcs.Task,
                "ConnectionException was not raised — fault from the discarded task was silently swallowed.");
            Assert.Same(expected, observed);
        }

        [Fact]
        public void CompletedTask_DoesNotRaiseConnectionException()
        {
            // Arrange — a successfully completed task must NOT trigger ConnectionException
            var connection = new NatsConnection(new NatsDefaultOptions());
            bool raised = false;
            connection.ConnectionException += (_, _) => raised = true;

            // Act
            connection.ObserveSubscribeTask(new ValueTask(Task.CompletedTask));

            // Assert — synchronous path; if it didn't raise synchronously it won't raise at all
            Assert.False(raised, "ConnectionException must not fire for a successfully completed task.");
        }

        [Fact]
        public async Task CancelledTask_DoesNotRaiseConnectionException()
        {
            // Arrange — cancellation (normal Unsubscribe path) must not be treated as an error
            var connection = new NatsConnection(new NatsDefaultOptions());

            bool raised = false;
            connection.ConnectionException += (_, _) => raised = true;

            var cancelledTask = new ValueTask(Task.FromCanceled(new System.Threading.CancellationToken(canceled: true)));

            // Act
            connection.ObserveSubscribeTask(cancelledTask);

            // Give the continuation time to NOT fire
            await Task.Delay(100);

            // Assert
            Assert.False(raised, "ConnectionException must not fire for a cancelled task (normal Unsubscribe path).");
        }
    }
}
