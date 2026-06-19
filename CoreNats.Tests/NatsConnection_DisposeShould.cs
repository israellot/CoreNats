namespace CoreNats.Tests
{
    using System.Threading.Tasks;
    using CoreNats.Messages;
    using Xunit;

    public class NatsConnection_DisposeShould
    {
        [Fact]
        public async Task CancelSynchronousInlineSubscriptions()
        {
            var connection = new NatsConnection(new NatsDefaultOptions());
            var unsubscriber = Assert.IsType<NatsUnsubscriber>(connection.Subscribe("subject", new NatsMessageInlineProcess((ref NatsInlineMsg _) => { })));
            var token = unsubscriber.Token;

            await connection.DisposeAsync();

            Assert.True(token.IsCancellationRequested);
        }
    }
}
