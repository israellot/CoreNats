namespace CoreNats.Tests.Messages
{
    using System.Linq;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Guards against ArgumentOutOfRangeException when ParseHeaders encounters a header
    /// line with no colon separator or no trailing CRLF.
    /// </summary>
    public class NatsMsgHeadersRead_MalformedShould
    {
        // Build a raw header block from a string, the same way the real parser receives it.
        private static NatsMsgHeadersRead Build(string raw)
        {
            var bytes = Encoding.UTF8.GetBytes(raw);
            return new NatsMsgHeadersRead(new System.ReadOnlyMemory<byte>(bytes));
        }

        [Fact]
        public void NotThrow_WhenHeaderLineHasNoColon()
        {
            // A valid header followed by a line that has no ':' at all.
            // ParseHeaders must break cleanly; it must NOT throw ArgumentOutOfRangeException.
            var raw = "NATS/1.0\r\nkey:value\r\nMalformedLineWithoutColon\r\n\r\n";
            var headers = Build(raw);

            // The valid header before the malformed one should still be readable.
            var all = headers.ReadAsString().ToList();
            Assert.Single(all);
            Assert.Equal("key", all[0].Key);
            Assert.Equal("value", all[0].Value);
        }

        [Fact]
        public void NotThrow_WhenHeaderLineHasNoTrailingCRLF()
        {
            // A valid header followed by a truncated line that has a colon but no '\r'.
            // ParseHeaders must break cleanly; it must NOT throw ArgumentOutOfRangeException.
            var raw = "NATS/1.0\r\nkey:value\r\ntruncated:no-crlf";
            var headers = Build(raw);

            // The valid header before the truncated one should still be readable.
            var all = headers.ReadAsString().ToList();
            Assert.Single(all);
            Assert.Equal("key", all[0].Key);
            Assert.Equal("value", all[0].Value);
        }

        [Fact]
        public void NotThrow_WhenEntireBlockIsTruncatedAfterProtocolLine()
        {
            // Header block that is cut off immediately after the protocol version line.
            var raw = "NATS/1.0\r\nno-colon-anywhere";
            var headers = Build(raw);

            Assert.Empty(headers.ReadAsString());
        }
    }
}
