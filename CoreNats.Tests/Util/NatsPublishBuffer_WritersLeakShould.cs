namespace CoreNats.Tests.Util
{
    using System;
    using System.Threading.Tasks;
    using CoreNats.Messages;
    using Xunit;

    public class NatsPublishBuffer_WritersLeakShould
    {
        /// <summary>
        /// A fake message whose Serialize always throws, used to exercise the
        /// _writers leak path in NatsPublishBuffer.TryWrite.
        /// </summary>
        private sealed class ThrowingMessage : INatsClientMessage
        {
            public int Length => 16; // non-zero so TryWrite reserves a slot

            public void Serialize(Span<byte> buffer)
                => throw new InvalidOperationException("Simulated serialize failure");
        }

        private sealed class ChangingLengthMessage : INatsClientMessage
        {
            private int _calls;

            public int Length => ++_calls < 3 ? 16 : -1;

            public void Serialize(Span<byte> buffer)
            {
            }
        }

        [Fact]
        public async Task CommitCompletesAfterSerializeThrows()
        {
            // Arrange
            var buffer = new NatsPublishBuffer(new byte[1024]);

            // Act – TryWrite should throw the serialize exception but must still
            //        decrement _writers before propagating.
            Assert.Throws<InvalidOperationException>(() =>
                buffer.TryWrite(new ThrowingMessage(), out _));

            // Assert – Commit() must not hang: if _writers were leaked it would
            //           spin-wait forever.  We race it against a 2-second timeout;
            //           the task should finish well within that window.
            var commitTask = buffer.Commit().AsTask();
            var winner = await Task.WhenAny(commitTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.True(winner == commitTask,
                "Commit() did not complete within 2 seconds – _writers counter was leaked by a throwing Serialize");

            // Propagate any unexpected exception from Commit itself
            await commitTask;
        }

        [Fact]
        public async Task CommitCompletesWhenLengthChangesAfterReservation()
        {
            var buffer = new NatsPublishBuffer(new byte[1024]);

            Assert.True(buffer.TryWrite(new ChangingLengthMessage(), out _));

            var commitTask = buffer.Commit().AsTask();
            var winner = await Task.WhenAny(commitTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.True(winner == commitTask,
                "Commit() did not complete within 2 seconds when Length changed after reservation");

            await commitTask;
        }
    }
}
