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
       
        public void Return()
        {            
            if (_owner is not null)
            {
                _owner.Return(_buffer);
            }
        }

    }

    internal class ProxyOwner<T> : IMemoryOwner<T>
    {
        public Memory<T> Memory => _memory;

        public ReadOnlyMemory<T> ReadOnlyMemory => _memory;

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
                _memory = null;
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