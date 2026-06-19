namespace CoreNats.Tests.Util
{
    using System;
    using System.Buffers;
    using Xunit;

    /// <summary>
    /// Verifies that ProxyOwner.Memory throws ObjectDisposedException after Dispose,
    /// preventing silent data-loss from the null-array coercion that made the
    /// pre-fix <c>_memory = null</c> assignment return Memory&lt;T&gt;.Empty instead.
    /// </summary>
    public class ProxyOwnerShould
    {
        private sealed class SimpleOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _data;
            public Memory<byte> Memory => _data.AsMemory();
            public SimpleOwner(int length) => _data = new byte[length];
            public void Dispose() { }
        }

        [Fact]
        public void ThrowObjectDisposedException_OnMemory_AfterDispose()
        {
            var owner = new SimpleOwner(8);
            // Use the internal Slice extension to get a ProxyOwner instance.
            var proxy = owner.Slice(0, 4);

            proxy.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = proxy.Memory);
        }

        [Fact]
        public void ThrowObjectDisposedException_OnMemory_AfterDoubleDispose()
        {
            var owner = new SimpleOwner(8);
            var proxy = owner.Slice(2);

            proxy.Dispose();
            proxy.Dispose(); // second dispose must be a no-op

            Assert.Throws<ObjectDisposedException>(() => _ = proxy.Memory);
        }

        [Fact]
        public void ReturnCorrectMemory_BeforeDispose()
        {
            var owner = new SimpleOwner(8);
            var proxy = owner.Slice(0, 4);

            var mem = proxy.Memory;

            Assert.Equal(4, mem.Length);
            proxy.Dispose();
        }
    }
}
