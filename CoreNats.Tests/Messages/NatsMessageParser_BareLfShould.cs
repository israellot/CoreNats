namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Text;
    using CoreNats.Messages;
    using Xunit;

    // Regression: ParseMessages used to strip the last byte of every line unconditionally,
    // assuming a CRLF terminator. A bare-LF-terminated line (no CR) therefore lost a real
    // content byte. With the conditional strip, the last byte survives when no CR precedes
    // the LF, while CRLF input keeps behaving exactly as before.
    public class NatsMessageParser_BareLfShould
    {
        private static INatsServerMessage ParseSingle(string wire)
        {
            var reader = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(wire));
            var outMessages = new INatsServerMessage[4];
            var parser = new NatsMessageParser();
            var count = parser.ParseMessages(reader, outMessages, out _, out _);
            Assert.Equal(1, count);
            return outMessages[0];
        }

        [Fact]
        public void KeepLastByteOnBareLfTerminatedLine()
        {
            // Bare LF, no CR. The closing '}' is the last byte; the old code dropped it,
            // leaving "INFO {" -> invalid JSON -> JsonException. The fix preserves it.
            var msg = ParseSingle("INFO {}\n");
            Assert.IsType<NatsInformation>(msg);
        }

        [Fact]
        public void StillParseCrlfTerminatedLine()
        {
            var msg = ParseSingle("INFO {}\r\n");
            Assert.IsType<NatsInformation>(msg);
        }
    }
}
