namespace CoreNats.Messages
{
    using System;
    using System.Buffers;
    using System.Runtime.CompilerServices;
    using System.Threading;


    public readonly ref struct NatsInlineMsg
    {

        public readonly NatsInlineKey Subject;
        public readonly NatsInlineKey ReplyTo;
        public readonly long SubscriptionId;
        public readonly ReadOnlySequence<byte> Payload;
        public readonly NatsMsgHeadersRead Headers;

        public NatsInlineMsg(ref NatsInlineKey subject, ref NatsInlineKey replyTo,long subscriptionId, ReadOnlySequence<byte> payload, NatsMsgHeadersRead headers)
        {
            Subject = subject;
            ReplyTo = replyTo;
            SubscriptionId = subscriptionId;
            Payload = payload;
            Headers = headers; 
        }


        public NatsMsg Persist()
        {
            return new NatsMsg(
                Subject.Span.ToArray(),
                SubscriptionId,
                ReplyTo.Span.ToArray(),
                Payload.ToArray(),
                Headers.Copy());
        }
    }

    public class NatsMsg : INatsServerMessage
    {
        
        public readonly NatsKey Subject;
        public readonly NatsKey ReplyTo;
        public readonly long SubscriptionId;

        public ReadOnlyMemory<byte> Payload { get; private set; }

        private NatsMsgHeadersRead? _headers;

        public NatsMsgHeadersRead Headers
        {
            get
            {
                if (_headers != null)
                    return _headers.Value;

               return NatsMsgHeadersRead.Empty;
            }
        }
        public NatsMsg(in NatsKey subject, in long subscriptionId, in NatsKey replyTo, ReadOnlyMemory<byte> payload)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
        }

        public NatsMsg(in NatsKey subject, in long subscriptionId, in NatsKey replyTo, ReadOnlyMemory<byte> payload, NatsMsgHeadersRead headers)
        {
            Subject = subject;
            SubscriptionId = subscriptionId;
            ReplyTo = replyTo;
            Payload = payload;
            _headers = headers;
        }

        

        internal NatsMsg Copy()
        {
            return new NatsMsg(Subject.Copy(), SubscriptionId, ReplyTo.Copy(), Payload.ToArray(),Headers.Copy());
        }

        

    }
}