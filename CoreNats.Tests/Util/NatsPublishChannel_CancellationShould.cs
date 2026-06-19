namespace CoreNats.Tests.Util
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    public class NatsPublishChannel_CancellationShould
    {
        /// <summary>
        /// A message whose serialized length exceeds the channel's default buffer (64 KB),
        /// so TryWrite always fails on the initial buffer and forces the backpressure path.
        /// </summary>
        private sealed class OversizedMessage : INatsClientMessage
        {
            // DefaultBufferLength is 1024 * 64; go one byte over so TryWrite always returns false.
            public int Length => 1024 * 64 + 1;
            public void Serialize(Span<byte> buffer) { /* no-op */ }
        }

        [Fact]
        public async Task ThrowOperationCanceledException_WhenChannelFullAndTokenAlreadyCancelled()
        {
            var pool = new NatsMemoryPool();
            var channel = new NatsPublishChannel(pool, CancellationToken.None);

            // Pre-fill the bounded channel to capacity using the public Writer so that
            // WaitToWriteAsync blocks (no consumer draining it).
            var dummyBuffer = new NatsPublishBuffer(new byte[1]);
            int channelCapacity = Math.Min(2, Environment.ProcessorCount);
            for (int i = 0; i < channelCapacity; i++)
            {
                Assert.True(channel.Writer.TryWrite(dummyBuffer),
                    $"Expected channel to accept slot {i + 1} of {channelCapacity}");
            }

            // Token cancelled before the call.
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // OversizedMessage.Length > DefaultBufferLength → TryWrite fails immediately
            // → PublishInternal enters WaitToWriteAsync(cancellationToken).
            // With the fix the cancelled token propagates and the ValueTask faults promptly.
            // Without the fix it would hang until a consumer drains a slot.
            var publishTask = channel.Publish(new OversizedMessage(), cts.Token).AsTask();

            // Give it 2 s to surface the exception; a hang means the token was dropped.
            var completed = await Task.WhenAny(publishTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.True(completed == publishTask,
                "Publish did not complete within 2 s — the cancellation token was likely not forwarded to WaitToWriteAsync.");

            // TaskCanceledException is a subclass of OperationCanceledException; both are valid.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishTask);
        }
    }
}
