namespace CoreNats.Tests.Util
{
    using System;
    using System.Text;
    using CoreNats;
    using Xunit;

    public class NatsPayload_StringCtorShould
    {
        [Fact]
        public void ContainUtf8Bytes_WhenConstructedFromNormalString()
        {
            var input = "hello";
            var expected = Encoding.UTF8.GetBytes(input);

            var payload = new NatsPayload(input);

            Assert.Equal(expected, payload.Memory.ToArray());
        }

        [Fact]
        public void HaveEmptyMemory_WhenConstructedFromNull()
        {
            var ex = Record.Exception(() =>
            {
                var payload = new NatsPayload((string)null);
                Assert.True(payload.IsEmpty);
                Assert.Equal(0, payload.Memory.Length);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void HaveEmptyMemory_WhenConstructedFromEmptyString()
        {
            var ex = Record.Exception(() =>
            {
                var payload = new NatsPayload(string.Empty);
                Assert.True(payload.IsEmpty);
                Assert.Equal(0, payload.Memory.Length);
            });
            Assert.Null(ex);
        }
    }
}
