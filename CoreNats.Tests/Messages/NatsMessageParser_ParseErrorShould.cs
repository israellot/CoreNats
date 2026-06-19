namespace CoreNats.Tests.Messages
{
    using System;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    public class NatsMessageParser_ParseErrorShould
    {
        // ParseMessages strips the trailing \r from each line before dispatching to ParseError.
        // These tests mirror that: input spans have no CRLF terminator.

        [Fact]
        public void ParseFullErrorMessage_UnknownSubject()
        {
            // "-ERR 'Unknown Subject'" — 22 bytes after \r is stripped
            // Slice(6, 22-7) = Slice(6, 15) = "Unknown Subject"
            var line = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("-ERR 'Unknown Subject'"));
            var err = new NatsMessageParser().ParseError(line);
            Assert.Equal("Unknown Subject", err.Error);
        }

        [Fact]
        public void ParseFullErrorMessage_StaleConnection()
        {
            // "-ERR 'Stale Connection'" — 23 bytes after \r is stripped
            // Slice(6, 23-7) = Slice(6, 16) = "Stale Connection"
            var line = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("-ERR 'Stale Connection'"));
            var err = new NatsMessageParser().ParseError(line);
            Assert.Equal("Stale Connection", err.Error);
        }

        [Fact]
        public void ParseEmptyError_ReturnsNullMessage()
        {
            // Bare "-ERR" with no quoted payload — fewer than 8 bytes, guard returns empty NatsError
            var line = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes("-ERR"));
            var err = new NatsMessageParser().ParseError(line);
            Assert.Null(err.Error);
        }
    }
}
