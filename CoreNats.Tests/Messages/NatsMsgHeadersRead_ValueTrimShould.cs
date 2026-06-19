namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Linq;
    using System.Text;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Verifies that NatsMsgHeadersRead.ParseHeaders correctly handles an optional
    /// leading space after the colon in header values, per NATS/HTTP convention.
    /// Real NATS servers send "Key: Value\r\n"; the parser must store "Value", not " Value".
    /// </summary>
    public class NatsMsgHeadersRead_ValueTrimShould
    {
        // Helper: build a full HMSG frame and parse it through ParseMessageWithHeader.
        private static NatsMsg ParseHmsgWithHeaders(string headerBlock)
        {
            // headerBlock is everything after "NATS/1.0\r\n" and before the payload.
            // e.g. "Nats-Status: 503\r\n"
            // Total header section: "NATS/1.0\r\n" + headerBlock + "\r\n" (blank line terminator)
            var headers = "NATS/1.0\r\n" + headerBlock + "\r\n";
            var payload = "Hello World";
            var body = headers + payload + "\r\n";

            var headerBytes = Encoding.UTF8.GetByteCount(headers);
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);
            // totallen in the HMSG command is headerSize + payloadSize (the trailing \r\n is NOT counted).
            var totalBytes = headerBytes + payloadBytes;

            // HMSG FOO.BAR 9 INBOX.34 <hdrlen> <totallen>
            var cmdLine = $"HMSG FOO.BAR 9 INBOX.34 {headerBytes} {totalBytes}";

            var reader = new SequenceReader<byte>(
                new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(body)));
            var message = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(cmdLine));

            return (NatsMsg)new NatsMessageParser().ParseMessageWithHeader(message, ref reader);
        }

        [Fact]
        public void TrimSingleLeadingSpaceAfterColon()
        {
            // NATS server canonical format: "Key: Value\r\n" — space after colon
            var msg = ParseHmsgWithHeaders("Nats-Status: 503\r\n");

            var headerList = msg.Headers.ReadAsString().ToList();
            Assert.Single(headerList);
            Assert.Equal("Nats-Status", headerList[0].Key);
            Assert.Equal("503", headerList[0].Value);   // must NOT be " 503"
        }

        [Fact]
        public void PreserveValueWhenNoSpaceAfterColon()
        {
            // Existing wire format used by some clients / test fixtures: "key:value\r\n" (no space)
            var msg = ParseHmsgWithHeaders("key:value\r\n");

            var headerList = msg.Headers.ReadAsString().ToList();
            Assert.Single(headerList);
            Assert.Equal("key", headerList[0].Key);
            Assert.Equal("value", headerList[0].Value);  // must still work as before
        }

        [Fact]
        public void HandleMultipleHeadersWithAndWithoutLeadingSpace()
        {
            // Mix of space and no-space in the same message
            var msg = ParseHmsgWithHeaders("X-No-Space:direct\r\nX-With-Space: padded\r\n");

            var headerList = msg.Headers.ReadAsString().ToList();
            Assert.Equal(2, headerList.Count);

            Assert.Equal("X-No-Space", headerList[0].Key);
            Assert.Equal("direct", headerList[0].Value);

            Assert.Equal("X-With-Space", headerList[1].Key);
            Assert.Equal("padded", headerList[1].Value);
        }
    }
}
