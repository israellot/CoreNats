namespace CoreNats.Messages
{
    using System;
    using System.Buffers.Text;


    public readonly struct NatsPub: INatsClientMessage
    {
        private const int CommandLength = 4;
        private const int DelimiterLength = 1;
        private const int EndLength = 2;

        private readonly int _length;
        public int Length => _length;

        private readonly NatsKey _subject;
        private readonly NatsKey _replyTo;
        private readonly NatsPayload _payload;

        public NatsPub(in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            _subject = subject;
            _replyTo = replyTo;
            _payload = payload;

            var hint = CommandLength; // PUB
            hint += subject.Memory.Length + 1; // Subject + space
            hint += replyTo.IsEmpty ? 0 : replyTo.Memory.Length + 1; // ReplyTo
            if (payload.Memory.Length < 10) hint += 1;
            else if (payload.Memory.Length < 100) hint += 2;
            else if (payload.Memory.Length < 1_000) hint += 3;
            else if (payload.Memory.Length < 10_000) hint += 4;
            else if (payload.Memory.Length < 100_000) hint += 5;
            else if (payload.Memory.Length < 1_000_000) hint += 6;
            else if (payload.Memory.Length < 10_000_000) hint += 7;
            else throw new ArgumentOutOfRangeException(nameof(payload));

            hint += EndLength; // Ending
            hint += payload.Memory.Length;
            hint += EndLength; // Ending Payload

            _length = hint;
        }

        public void Serialize(Span<byte> buffer)
        {
            buffer[0] = (byte)'P';
            buffer[1] = (byte)'U';
            buffer[2] = (byte)'B';
            buffer[3] = (byte)' ';
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

            Utf8Formatter.TryFormat(_payload.Memory.Length, buffer.Slice(consumed), out var written);
            consumed += written;
            buffer[consumed++] = (byte)'\r';
            buffer[consumed++] = (byte)'\n';
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
        internal static ReadOnlyMemory<byte> Serialize(in NatsKey subject, in NatsKey replyTo, in NatsPayload payload)
        {
            var pub = new NatsPub(subject, replyTo, payload);
            var buffer = new byte[pub.Length];
            pub.Serialize(buffer);

            // payload.Owner already disposed by instance Serialize above

            return buffer;
        }
      
        
    }
}