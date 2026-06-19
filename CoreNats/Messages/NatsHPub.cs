namespace CoreNats.Messages
{
    using System;
    using System.Buffers.Text;


    public readonly struct NatsHPub:INatsClientMessage
    {
        private const int CommandLength = 5;
        private const int EndLength = 2;
                
        public  int Length => _length;

        private readonly int _length;
        private readonly NatsKey _subject;
        private readonly NatsKey _replyTo;
        private readonly NatsMsgHeaders _header;
        private readonly NatsPayload _payload;

        public NatsHPub(in NatsKey subject, in NatsKey replyTo, in NatsMsgHeaders header, in NatsPayload payload)
        {
            _subject = subject;
            _replyTo = replyTo;
            _header = header;
            _payload = payload;

            var totalLength = payload.Memory.Length + header.SerializedLength;

            var hint = CommandLength; // HPUB
            hint += subject.Memory.Length + 2; // Subject + spaces
            hint += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo

            if (header.SerializedLength < 10) hint += 1;
            else if (header.SerializedLength < 100) hint += 2;
            else if (header.SerializedLength < 1_000) hint += 3;
            else if (header.SerializedLength < 10_000) hint += 4;
            else if (header.SerializedLength < 100_000) hint += 5;
            else if (header.SerializedLength < 1_000_000) hint += 6;
            else if (header.SerializedLength < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(header));

            if (totalLength < 10) hint += 1;
            else if (totalLength < 100) hint += 2;
            else if (totalLength < 1_000) hint += 3;
            else if (totalLength < 10_000) hint += 4;
            else if (totalLength < 100_000) hint += 5;
            else if (totalLength < 1_000_000) hint += 6;
            else if (totalLength < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(payload));



            hint += EndLength; // Ending
            hint += payload.Memory.Length;
            hint += header.SerializedLength;
            hint += EndLength; // Ending Payload

            _length = hint;
        }

        public void Serialize(Span<byte> buffer)
        {
          

            buffer[0] = (byte)'H';
            buffer[1] = (byte)'P';
            buffer[2] = (byte)'U';
            buffer[3] = (byte)'B';
            buffer[4] = (byte)' ';
            var consumed = CommandLength;
            _subject.Memory.Span.CopyTo(buffer.Slice(consumed));
            consumed += _subject.Memory.Length;
            buffer[consumed++] = (byte)' ';
            if (!_replyTo.IsEmpty)
            {
                _replyTo.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _replyTo.Memory.Length;
                buffer[consumed++] = (byte)' ';
            }

            Utf8Formatter.TryFormat(_header.SerializedLength, buffer.Slice(consumed), out var written);
            consumed += written;
            buffer[consumed++] = (byte)' ';

            Utf8Formatter.TryFormat(_payload.Memory.Length + _header.SerializedLength, buffer.Slice(consumed), out written);
            consumed += written;
            buffer[consumed++] = (byte)'\r';
            buffer[consumed++] = (byte)'\n';

            _header.SerializeTo(buffer.Slice(consumed));
            consumed += _header.SerializedLength;

            if (!_payload.IsEmpty)
            {
                _payload.Memory.Span.CopyTo(buffer.Slice(consumed));
                consumed += _payload.Memory.Length;
            }

            buffer[consumed++] = (byte)'\r';
            buffer[consumed] = (byte)'\n';

            _payload.Owner?.Dispose();
        }

        //test and debug only
        internal static ReadOnlyMemory<byte> Serialize(in NatsKey subject, in NatsKey replyTo, in NatsMsgHeaders header,in NatsPayload payload)
        {
            var pub = new NatsHPub(subject, replyTo, header,payload);
            var buffer = new byte[pub.Length];
            pub.Serialize(buffer);

            // payload.Owner already disposed by instance Serialize above

            return buffer;
        }

    }
}