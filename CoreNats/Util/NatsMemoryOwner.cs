namespace CoreNats
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Runtime.InteropServices;

    public readonly struct NatsMemoryOwner
    {
        public static readonly NatsMemoryOwner Empty= new NatsMemoryOwner(new byte[0]);

        public readonly Memory<byte> Memory;

        private readonly ArrayPool<byte>? _owner;        

        private readonly byte[]? _buffer;

        internal NatsMemoryOwner(ArrayPool<byte> owner, int length)
        {
            _owner = owner;
            _buffer = owner.Rent(length);
            Memory = _buffer.AsMemory(0, length);
        }

        internal NatsMemoryOwner(byte[] buffer)
        {
            _owner = null;
            _buffer = null;
            Memory = buffer.AsMemory();
        }
       
        /// <summary>
        /// Returns the rented buffer back to the <see cref="ArrayPool{T}"/>.
        /// This method is a safe no-op when called on a default/empty instance
        /// (i.e. when no pool buffer was rented).
        ///
        /// <para>
        /// <strong>Value-type copy warning:</strong> Because <see cref="NatsMemoryOwner"/>
        /// is a <c>readonly struct</c>, every assignment creates an independent copy that
        /// holds the same <c>_owner</c> and <c>_buffer</c> references.  Calling
        /// <c>Return()</c> on two copies of the same instance will return the same
        /// array to the pool twice, which is undefined behaviour and can cause silent
        /// memory corruption.  Callers must ensure that only one copy of a pool-backed
        /// owner is ever returned.
        /// </para>
        /// </summary>
        public void Return()
        {
            // Guard both _owner and _buffer: _buffer should always be non-null when
            // _owner is non-null (they are set together in the pool constructor), but
            // an explicit null check prevents passing null to ArrayPool.Return() if
            // the struct is ever in an inconsistent state (e.g. constructed via
            // reflection or deserialization).
            if (_owner is not null && _buffer is not null)
            {
                _owner.Return(_buffer);
            }
        }

    }

    internal class ProxyOwner<T> : IMemoryOwner<T>
    {
        /// <summary>
        /// Returns the memory slice owned by this instance.
        /// Throws <see cref="ObjectDisposedException"/> if the owner has already been disposed,
        /// preventing silent data loss from the null-array coercion that makes
        /// <c>_memory = null</c> compile as <c>Memory&lt;T&gt;.Empty</c>.
        /// </summary>
        public Memory<T> Memory
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ProxyOwner<T>));
                return _memory;
            }
        }

        public ReadOnlyMemory<T> ReadOnlyMemory
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ProxyOwner<T>));
                return _memory;
            }
        }

        public IMemoryOwner<T>? Parent => _owner;


        private Memory<T> _memory;


        private IMemoryOwner<T>? _owner;

        public ProxyOwner(IMemoryOwner<T> owner, Memory<T> memory)
        {
            _memory = memory;
            _owner = owner;
        }

        public ProxyOwner(IMemoryOwner<T> owner)
        {
            _memory = owner.Memory;
            _owner = owner;
        }

        object _disposeLock = new object();
        bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;

                _owner?.Dispose();
                _owner = null;
                _memory = default;  // clear reference for GC; access is now guarded by _disposed check above
            }

        }

    }

    internal static class MemoryOwnerExtensions
    {
        public static IMemoryOwner<T> Slice<T>(this IMemoryOwner<T> o, int start)
        {
            if (start > 0)
                return new ProxyOwner<T>(o, o.Memory.Slice(start));
            else
                return o;
        }

        public static IMemoryOwner<T> Slice<T>(this IMemoryOwner<T> o, int start, int length)
        {
            if (start == 0 && o.Memory.Length == length)
                return o;
            else
                return new ProxyOwner<T>(o, o.Memory.Slice(start, length));
        }

        
    }

}