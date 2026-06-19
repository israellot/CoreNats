namespace CoreNats.Tests.Util
{
    using System;
    using System.Buffers;
    using Xunit;

    /// <summary>
    /// Tests for NatsMemoryOwner.Return() guard behaviour.
    ///
    /// Background: NatsMemoryOwner is a readonly struct wrapping an ArrayPool-rented
    /// buffer.  Return() must be a safe no-op when called on a default/empty instance
    /// (no pool buffer was rented), and must return a rented buffer exactly once when
    /// called on a pool-backed instance.
    ///
    /// Note on double-return: because NatsMemoryOwner is a value type, struct copies
    /// share the same _owner/_buffer references; calling Return() on two copies of a
    /// pool-backed instance would return the same array to the pool twice (undefined
    /// behaviour). That is a caller-contract issue inherent to value types and is
    /// documented on Return() but cannot be prevented by a guard inside the method.
    /// These tests cover the cases that CAN be guarded: default struct and raw-buffer
    /// instances do not result in ArrayPool.Return(null) or any exception.
    /// </summary>
    public class NatsMemoryOwner_ReturnShould
    {
        [Fact]
        public void Return_OnDefaultInstance_DoesNotThrow()
        {
            // default(NatsMemoryOwner) has _owner == null and _buffer == null.
            // Return() must be a silent no-op — it must not throw ArgumentNullException
            // from ArrayPool.Return(null).
            var defaultOwner = default(NatsMemoryOwner);
            var ex = Record.Exception(() => defaultOwner.Return());
            Assert.Null(ex);
        }

        [Fact]
        public void Return_OnEmptyStaticInstance_DoesNotThrow()
        {
            // NatsMemoryOwner.Empty is constructed via the raw-buffer ctor (byte[]),
            // which sets _owner = null.  Return() must be a safe no-op.
            var ex = Record.Exception(() => NatsMemoryOwner.Empty.Return());
            Assert.Null(ex);
        }

        [Fact]
        public void Return_OnEmptyStaticInstance_CanBeCalledMultipleTimes()
        {
            // Repeated Return() on an empty instance must not throw.
            var ex = Record.Exception(() =>
            {
                NatsMemoryOwner.Empty.Return();
                NatsMemoryOwner.Empty.Return();
                NatsMemoryOwner.Empty.Return();
            });
            Assert.Null(ex);
        }

        [Fact]
        public void Return_OnPoolBackedInstance_ReturnsBufferToPool()
        {
            // Verify that a pool-backed owner actually hands the buffer back.
            // We use a custom tracking pool to confirm Return() is called exactly once.
            var trackingPool = new TrackingArrayPool();
            var owner = new NatsMemoryOwner(trackingPool, 64);

            Assert.Equal(0, trackingPool.ReturnCount);
            owner.Return();
            Assert.Equal(1, trackingPool.ReturnCount);
        }

        [Fact]
        public void Return_OnPoolBackedInstance_DoesNotPassNullToPool()
        {
            // Ensure the guarded path never passes a null array to ArrayPool.Return().
            var strictPool = new NullRejectingArrayPool();
            var owner = new NatsMemoryOwner(strictPool, 32);
            // Should not throw ArgumentNullException from the null guard in Return().
            var ex = Record.Exception(() => owner.Return());
            Assert.Null(ex);
        }

        // ---------------------------------------------------------------------------
        // Helpers

        /// <summary>Tracks how many times Return() is called.</summary>
        private sealed class TrackingArrayPool : ArrayPool<byte>
        {
            public int ReturnCount { get; private set; }

            public override byte[] Rent(int minimumLength) =>
                ArrayPool<byte>.Shared.Rent(minimumLength);

            public override void Return(byte[] array, bool clearArray = false)
            {
                ReturnCount++;
                ArrayPool<byte>.Shared.Return(array, clearArray);
            }
        }

        /// <summary>
        /// Throws if Return() is called with a null array, mirroring the behaviour
        /// of the real ArrayPool implementation.
        /// </summary>
        private sealed class NullRejectingArrayPool : ArrayPool<byte>
        {
            public override byte[] Rent(int minimumLength) =>
                ArrayPool<byte>.Shared.Rent(minimumLength);

            public override void Return(byte[] array, bool clearArray = false)
            {
                if (array is null)
                    throw new ArgumentNullException(nameof(array), "ArrayPool.Return must not be called with null.");
                ArrayPool<byte>.Shared.Return(array, clearArray);
            }
        }
    }
}
