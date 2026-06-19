namespace CoreNats.Tests.Messages
{
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Text;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    public class NatsMessageParser_InlineHeaderShould
    {
        // One HMSG frame: command line + CRLF + headers (23 bytes) + payload ("Hello World") + CRLF.
        private const string HMsgFrame =
            "HMSG FOO.BAR 9 INBOX.34 23 34\r\nNATS/1.0\r\nkey:value\r\n\r\nHello World\r\n";

        private static NatsMessageParser ParserWithSubscription(long subscriptionId, NatsMessageInlineProcess process)
        {
            var subscriptions = new ConcurrentDictionary<long, NatsConnection.InlineSubscription>();
            subscriptions[subscriptionId] = new NatsConnection.InlineSubscription(
                new NatsKey("FOO.BAR"), null, subscriptionId, process);
            return new NatsMessageParser(null, subscriptions);
        }

        // Regression: ParseMessageWithHeaderInline used to advance the reader twice when a
        // matching subscription existed (once inside the `if`, once unconditionally after it),
        // so a buffer holding two HMSG frames only delivered the first and reported a `consumed`
        // count that skipped past the second frame.
        [Fact]
        public void DeliverEveryHeaderMessageWhenSubscriptionMatches()
        {
            var delivered = 0;
            var parser = ParserWithSubscription(9, (ref NatsInlineMsg msg) =>
            {
                delivered++;
                Assert.Equal("FOO.BAR", msg.Subject.AsString());
                Assert.Equal("Hello World", Encoding.UTF8.GetString(msg.Payload.FirstSpan));
            });

            var bytes = Encoding.UTF8.GetBytes(HMsgFrame + HMsgFrame);
            var buffer = new ReadOnlySequence<byte>(bytes);
            var outMessages = new INatsServerMessage[8];

            var produced = parser.ParseMessages(buffer, outMessages, out var consumed, out var inlined);

            Assert.Equal(0, produced);            // inline subscriptions don't surface as out-messages
            Assert.Equal(2, inlined);             // both frames dispatched
            Assert.Equal(2, delivered);           // handler invoked once per frame
            Assert.Equal(bytes.Length, consumed); // entire buffer consumed, no over-advance
        }
    }
}
