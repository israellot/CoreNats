namespace CoreNats.Tests
{
    using System;
    using System.Text;
    using System.Text.Json;
    using Xunit;

    public class NatsDefaultSerializerShould
    {
        private sealed class TestPayload
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        [Fact]
        public void Serialize_ReturnsUtf8Json()
        {
            var serializer = new NatsDefaultSerializer();
            var payload = new TestPayload { Name = "sample", Count = 42 };

            var bytes = serializer.Serialize(payload);

            using var document = JsonDocument.Parse(bytes);
            Assert.Equal("sample", document.RootElement.GetProperty("Name").GetString());
            Assert.Equal(42, document.RootElement.GetProperty("Count").GetInt32());
        }

        [Fact]
        public void Deserialize_ReturnsObject()
        {
            var serializer = new NatsDefaultSerializer();
            var json = Encoding.UTF8.GetBytes("{\"Name\":\"sample\",\"Count\":42}");

            var payload = serializer.Deserialize<TestPayload>(json);

            Assert.Equal("sample", payload.Name);
            Assert.Equal(42, payload.Count);
        }

        [Fact]
        public void Deserialize_InvalidJson_ThrowsJsonException()
        {
            var serializer = new NatsDefaultSerializer();
            var json = Encoding.UTF8.GetBytes("{not-json}");

            Assert.Throws<JsonException>(() => serializer.Deserialize<TestPayload>(json));
        }
    }
}
