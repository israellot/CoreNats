namespace CoreNats.Tests.Messages
{
    using System.Buffers;
    using System.Net;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    public class NatsMessageParser_EdgeCasesShould
    {
        private static ReadOnlySequence<byte> AsSequence(string value)
        {
            return new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(value));
        }

        [Fact]
        public void ParseMessages_WithPartialMsgPayload_ReturnsZeroAndConsumesNothing()
        {
            var parser = new NatsMessageParser();
            var messages = new INatsServerMessage[4];

            var count = parser.ParseMessages(AsSequence("MSG FOO.BAR 1 5\r\nhel"), messages, out var consumed, out var inlined);

            Assert.Equal(0, count);
            Assert.Equal(0, consumed);
            Assert.Equal(0, inlined);
        }

        [Fact]
        public void ParseMessages_WithPartialHmsgPayload_ReturnsZeroAndConsumesNothing()
        {
            var parser = new NatsMessageParser();
            var messages = new INatsServerMessage[4];

            var count = parser.ParseMessages(AsSequence("HMSG FOO.BAR 1 12 17\r\nNATS/1.0\r\n\r\nhel"), messages, out var consumed, out var inlined);

            Assert.Equal(0, count);
            Assert.Equal(0, consumed);
            Assert.Equal(0, inlined);
        }

        [Fact]
        public void ParseMessages_WithSmallOutputBuffer_StopsAtBufferLength()
        {
            var parser = new NatsMessageParser();
            var messages = new INatsServerMessage[1];

            var count = parser.ParseMessages(AsSequence("PING\r\nPONG\r\n"), messages, out var consumed, out var inlined);

            Assert.Equal(1, count);
            Assert.IsType<NatsPing>(messages[0]);
            Assert.Equal(6, consumed);
            Assert.Equal(0, inlined);
        }

        [Fact]
        public void ParseMessages_WithUnknownMessage_ThrowsProtocolViolationException()
        {
            var parser = new NatsMessageParser();
            var messages = new INatsServerMessage[4];

            Assert.Throws<ProtocolViolationException>(() => parser.ParseMessages(AsSequence("NOPE\r\n"), messages, out _, out _));
        }

        [Fact]
        public void ParseMessageWithHeader_WithZeroPayload_ReturnsEmptyPayload()
        {
            var headers = "NATS/1.0\r\nkey:value\r\n\r\n";
            var headerBytes = Encoding.UTF8.GetByteCount(headers);
            var reader = new SequenceReader<byte>(AsSequence(headers + "\r\n"));
            var line = Encoding.UTF8.GetBytes($"HMSG FOO.BAR 1 {headerBytes} {headerBytes}");

            var message = new NatsMessageParser().ParseMessageWithHeader(line, ref reader);

            Assert.NotNull(message);
            Assert.Equal(0, message.Payload.Length);
            Assert.True(message.Headers.TryGetValueAsString("key", out var value));
            Assert.Equal("value", value);
        }

        [Fact]
        public void ParseMessageWithHeader_WithMissingBody_ReturnsNull()
        {
            var reader = new SequenceReader<byte>(AsSequence("NATS/1.0\r\n"));
            var line = Encoding.UTF8.GetBytes("HMSG FOO.BAR 1 12 17");

            var message = new NatsMessageParser().ParseMessageWithHeader(line, ref reader);

            Assert.Null(message);
            Assert.Equal(0, reader.Consumed);
        }
    }
}
