namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Net;
    using System.Text;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Guards for malformed / short protocol lines in ParseMessages:
    ///   (a) bare \r\n  → empty line after \r strip → skip (no throw)
    ///   (b) bare \n    → empty span, Length==0 before strip → skip (no throw)
    ///   (c) P\r\n      → 1-byte 'P' line; line[1] was unguarded → ProtocolViolationException
    ///   (d) valid line after an empty line still parses correctly
    /// </summary>
    public class NatsMessageParser_MalformedInputShould
    {
        private static ReadOnlySequence<byte> AsSequence(string s)
            => new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(s));

        private static (int count, long consumed) Run(string input, int slots = 16)
        {
            var parser = new NatsMessageParser();
            var messages = new INatsServerMessage[slots];
            var count = parser.ParseMessages(AsSequence(input), messages, out long consumed, out _);
            return (count, consumed);
        }

        /// <summary>
        /// A lone \r\n (bare empty line) must not throw and must produce zero messages.
        /// </summary>
        [Fact]
        public void BareCarriageReturnLineFeed_DoesNotThrow_ProducesNoMessages()
        {
            var (count, _) = Run("\r\n");
            Assert.Equal(0, count);
        }

        /// <summary>
        /// A lone \n (no preceding \r) must not throw — Length==0 before the \r strip
        /// triggers the same empty-line guard.
        /// </summary>
        [Fact]
        public void BareLineFeed_DoesNotThrow_ProducesNoMessages()
        {
            var (count, _) = Run("\n");
            Assert.Equal(0, count);
        }

        /// <summary>
        /// A single-byte 'P' followed by \r\n is a truncated PING/PONG line.
        /// line[1] was previously unguarded; now throws ProtocolViolationException.
        /// </summary>
        [Fact]
        public void TruncatedPLine_ThrowsProtocolViolationException()
        {
            Assert.Throws<ProtocolViolationException>(() => Run("P\r\n"));
        }

        /// <summary>
        /// A well-formed PING line after an empty line must still parse correctly.
        /// </summary>
        [Fact]
        public void WellFormedMessageAfterEmptyLine_ParsesCorrectly()
        {
            // Empty line then a valid PING
            var (count, _) = Run("\r\nPING\r\n");
            Assert.Equal(1, count);
        }

        /// <summary>
        /// A well-formed PONG line after a bare \n must still parse correctly.
        /// </summary>
        [Fact]
        public void WellFormedMessageAfterBareLf_ParsesCorrectly()
        {
            var (count, _) = Run("\nPONG\r\n");
            Assert.Equal(1, count);
        }
    }
}
