namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Text;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Verifies that the static Serialize helpers on NatsPub and NatsHPub dispose the
    /// payload's IMemoryOwner exactly once.
    ///
    /// Background: instance Serialize(Span&lt;byte&gt;) calls _payload.Owner?.Dispose() at the end
    /// of serialization — that is the intended disposal site (production callers in
    /// NatsPublishBuffer.TryWrite rely on it).  The static helpers previously called
    /// payload.Owner?.Dispose() a second time after calling instance Serialize, causing a
    /// double-dispose for any pooled IMemoryOwner — the underlying array could be returned to
    /// the pool twice and handed to two concurrent renters simultaneously (silent corruption).
    /// </summary>
    public class NatsPub_SerializeDisposeShould
    {
        private sealed class CountingOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _data;
            public int DisposeCount { get; private set; }

            public Memory<byte> Memory => _data.AsMemory();

            public CountingOwner(byte[] data) => _data = data;

            public void Dispose() => DisposeCount++;
        }

        [Fact]
        public void DisposeOwnerExactlyOnce_NatsPub()
        {
            var bytes = Encoding.UTF8.GetBytes("Hello NATS!");
            var owner = new CountingOwner(bytes);
            var payload = new NatsPayload(owner);

            NatsPub.Serialize("FOO", NatsKey.Empty, payload);

            Assert.Equal(1, owner.DisposeCount);
        }

        [Fact]
        public void DisposeOwnerExactlyOnce_NatsPub_WithReplyTo()
        {
            var bytes = Encoding.UTF8.GetBytes("Knock Knock");
            var owner = new CountingOwner(bytes);
            var payload = new NatsPayload(owner);

            NatsPub.Serialize("FRONT.DOOR", "INBOX.22", payload);

            Assert.Equal(1, owner.DisposeCount);
        }

        [Fact]
        public void DisposeOwnerExactlyOnce_NatsHPub()
        {
            var bytes = Encoding.UTF8.GetBytes("Hello NATS!");
            var owner = new CountingOwner(bytes);
            var payload = new NatsPayload(owner);
            NatsMsgHeaders headers = new Dictionary<string, string>() { ["key"] = "value" };

            NatsHPub.Serialize("FOO", NatsKey.Empty, headers, payload);

            Assert.Equal(1, owner.DisposeCount);
        }

        [Fact]
        public void DisposeOwnerExactlyOnce_NatsHPub_WithReplyTo()
        {
            var bytes = Encoding.UTF8.GetBytes("Knock Knock");
            var owner = new CountingOwner(bytes);
            var payload = new NatsPayload(owner);
            NatsMsgHeaders headers = new Dictionary<string, string>() { ["key"] = "value" };

            NatsHPub.Serialize("FRONT.DOOR", "INBOX.22", headers, payload);

            Assert.Equal(1, owner.DisposeCount);
        }

        [Fact]
        public void NullOwner_DoesNotThrow_NatsPub()
        {
            // NatsPayload without an owner — should not throw
            var payload = new NatsPayload(Encoding.UTF8.GetBytes("Hello NATS!"));
            var result = NatsPub.Serialize("FOO", NatsKey.Empty, payload);
            Assert.False(result.IsEmpty);
        }

        [Fact]
        public void NullOwner_DoesNotThrow_NatsHPub()
        {
            var payload = new NatsPayload(Encoding.UTF8.GetBytes("Hello NATS!"));
            NatsMsgHeaders headers = new Dictionary<string, string>() { ["key"] = "value" };
            var result = NatsHPub.Serialize("FOO", NatsKey.Empty, headers, payload);
            Assert.False(result.IsEmpty);
        }
    }
}
