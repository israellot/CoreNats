namespace CoreNats
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
   using CoreNats.Messages;

    public interface INatsConnection : IAsyncDisposable
    {
        INatsOptions Options { get; }
        NatsStatus Status { get; }

        event EventHandler<Exception>? ConnectionException;
        event EventHandler<NatsStatus>? StatusChange;
        event EventHandler<NatsInformation>? ConnectionInformation;

        long SenderQueueSize { get; }
        long ReceiverQueueSize { get; }
        long TransmitBytesTotal { get; }
        long ReceivedBytesTotal { get; }
        long TransmitMessagesTotal { get; }
        long ReceivedMessagesTotal { get; }

        ValueTask ConnectAsync();
        ValueTask DisconnectAsync();
        ValueTask PublishAsync(in NatsKey subject, in NatsPayload? payload = null, in NatsKey? replyTo = null, in NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default);
        ValueTask SubscribeAsync(NatsKey subject, NatsMessageProcess process, NatsKey? queueGroup = null, CancellationToken cancellationToken = default);

    }
}