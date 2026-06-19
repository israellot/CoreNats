namespace CoreNats.Tests.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    public class NatsMsgHeadersShould
    {
        [Fact]
        public void Constructor_WithEmptyKey_ThrowsArgumentException()
        {
            var headers = new[] { new KeyValuePair<string, string>(string.Empty, "value") };

            Assert.Throws<ArgumentException>(() => new NatsMsgHeaders(headers));
        }

        [Fact]
        public void Constructor_WithColonInKey_ThrowsArgumentException()
        {
            var headers = new[] { new KeyValuePair<string, string>("bad:key", "value") };

            Assert.Throws<ArgumentException>(() => new NatsMsgHeaders(headers));
        }

        [Fact]
        public void Constructor_WithControlCharacterInKey_ThrowsArgumentException()
        {
            var headers = new[] { new KeyValuePair<string, string>("bad\u001fkey", "value") };

            Assert.Throws<ArgumentException>(() => new NatsMsgHeaders(headers));
        }

        [Fact]
        public void Constructor_WithInvalidControlCharacterInValue_ThrowsArgumentException()
        {
            var headers = new[] { new KeyValuePair<string, string>("key", "bad\u001fvalue") };

            Assert.Throws<ArgumentException>(() => new NatsMsgHeaders(headers));
        }

        [Fact]
        public void Constructor_WithTabInValue_AllowsValue()
        {
            var headers = new[] { new KeyValuePair<string, string>("key", "tab\tvalue") };

            var natsHeaders = new NatsMsgHeaders(headers);

            Assert.False(natsHeaders.IsEmpty);
        }

        [Fact]
        public void SerializeTo_WithMultipleHeaders_WritesExpectedWireFormat()
        {
            var headers = new NatsMsgHeaders(new[]
            {
                new KeyValuePair<string, string>("first", "one"),
                new KeyValuePair<string, string>("second", "two")
            });
            var buffer = new byte[headers.SerializedLength];

            headers.SerializeTo(buffer);

            Assert.Equal("NATS/1.0\r\nfirst:one\r\nsecond:two\r\n\r\n", Encoding.UTF8.GetString(buffer));
        }
    }
}
