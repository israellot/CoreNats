namespace CoreNats.Tests.Util
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using CoreNats;
    using Xunit;

    public class NatsPublishBuffer_CommitOnceShould
    {
        [Fact]
        public async Task FireOnCommitExactlyOnce_WhenCalledSequentially()
        {
            var buffer = new NatsPublishBuffer(new byte[1024]);
            var callCount = 0;
            buffer.OnCommit += () => Interlocked.Increment(ref callCount);

            await buffer.Commit();
            await buffer.Commit();

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task FireOnCommitExactlyOnce_WhenCalledConcurrently()
        {
            const int threadCount = 8;
            for (var trial = 0; trial < 20; trial++)
            {
                var buffer = new NatsPublishBuffer(new byte[1024]);
                var callCount = 0;
                buffer.OnCommit += () => Interlocked.Increment(ref callCount);

                var barrier = new Barrier(threadCount);
                var tasks = new Task[threadCount];
                for (var i = 0; i < threadCount; i++)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        barrier.SignalAndWait();
                        await buffer.Commit();
                    });
                }

                await Task.WhenAll(tasks);

                Assert.Equal(1, callCount);
            }
        }

        [Fact]
        public async Task NotFireOnCommit_AfterReset()
        {
            var buffer = new NatsPublishBuffer(new byte[1024]);
            var callCount = 0;
            buffer.OnCommit += () => Interlocked.Increment(ref callCount);

            await buffer.Commit();
            Assert.Equal(1, callCount);

            buffer.Reset();

            await buffer.Commit();
            Assert.Equal(2, callCount);
        }
    }
}
