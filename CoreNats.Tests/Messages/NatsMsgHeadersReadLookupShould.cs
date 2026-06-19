namespace CoreNats.Tests.Messages
{
    using System.Linq;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    public class NatsMsgHeadersReadLookupShould
    {
        private static NatsMsgHeadersRead CreateHeaders(string headers)
        {
            return new NatsMsgHeadersRead(Encoding.UTF8.GetBytes("NATS/1.0\r\n" + headers + "\r\n"));
        }

        [Fact]
        public void TryGetValue_WithByteKey_ReturnsMatchingValue()
        {
            var headers = CreateHeaders("key:value\r\nother:ignored\r\n");

            var found = headers.TryGetValue(Encoding.UTF8.GetBytes("key"), out var value);

            Assert.True(found);
            Assert.Equal("value", Encoding.UTF8.GetString(value.Span));
        }

        [Fact]
        public void TryGetValue_WithStringKey_ReturnsMatchingValue()
        {
            var headers = CreateHeaders("key:value\r\n");

            var found = headers.TryGetValue("key", out var value);

            Assert.True(found);
            Assert.Equal("value", Encoding.UTF8.GetString(value.Span));
        }

        [Fact]
        public void TryGetValueAsString_WithMissingKey_ReturnsFalse()
        {
            var headers = CreateHeaders("key:value\r\n");

            var found = headers.TryGetValueAsString("missing", out var value);

            Assert.False(found);
            Assert.Null(value);
        }

        [Fact]
        public void ReadAsBytes_ReturnsOriginalHeaderSlices()
        {
            var headers = CreateHeaders("key:value\r\n");

            var header = headers.ReadAsBytes().Single();

            Assert.Equal("key", Encoding.UTF8.GetString(header.Key.Span));
            Assert.Equal("value", Encoding.UTF8.GetString(header.Value.Span));
        }

        [Fact]
        public void Copy_CreatesIndependentHeaderData()
        {
            var data = Encoding.UTF8.GetBytes("NATS/1.0\r\nkey:value\r\n\r\n");
            var headers = new NatsMsgHeadersRead(data);

            var copy = headers.Copy();
            data[13] = (byte)'X';

            Assert.True(copy.TryGetValueAsString("key", out var value));
            Assert.Equal("value", value);
        }
    }
}
