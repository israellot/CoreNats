namespace CoreNats
{
    using System;

    public interface INatsSerializer
    {
        byte[] Serialize<T>(T obj);
        T Deserialize<T>(ReadOnlyMemory<byte> buffer);
    }
}