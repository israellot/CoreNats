namespace CoreNats
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO.Pipelines;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Messages;
    using Microsoft.Extensions.Logging;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Buffers;
    using System.Diagnostics;

    public class NatsConnection : INatsConnection
    {
        public NatsInformation? NatsInformation { get; private set; }

        private static long _nextSubscriptionId = 1;

        private readonly ILogger<NatsConnection>? _logger;

        private long _senderQueueSize;
        private long _receiverQueueSize;
        private long _transmitBytesTotal;
        private long _receivedBytesTotal;
        private long _transmitMessagesTotal;
        private long _receivedMessagesTotal;

        private Task? _readWriteAsyncTask;
        private CancellationTokenSource? _disconnectSource;
        private CancellationTokenSource? _debugDisconnectSource;
        private TaskCompletionSource<bool>? _connectionInfoTcs;
        private ReadOnlyMemory<byte> _reconnectResendBuffer = ReadOnlyMemory<byte>.Empty;

        private readonly NatsPublishChannel _senderChannel;
        private readonly NatsMemoryPool _memoryPool;
        private readonly ConcurrentDictionary<long, InlineSubscription> _inlineSubscriptions = new ConcurrentDictionary<long, InlineSubscription>();

        private class Subscription
        {
            
            private readonly Channel<NatsMsg> _channel;

            public Subscription(NatsKey subject, NatsKey? queueGroup, long subscriptionId, int queueLength)
            {
                Subject = subject;
                QueueGroup = queueGroup ?? NatsKey.Empty;
                SubscriptionId = subscriptionId;

                _channel = Channel.CreateBounded<NatsMsg>(
                    new BoundedChannelOptions(queueLength)
                    {
                        SingleReader = true,
                        SingleWriter = true,
                        AllowSynchronousContinuations = true
                    });


                Writer = _channel.Writer;
                Reader = _channel.Reader;
            }

            public NatsKey Subject { get; }
            public NatsKey QueueGroup { get; }
            public long SubscriptionId { get; }
            
            public ChannelWriter<NatsMsg> Writer { get; }
            public ChannelReader<NatsMsg> Reader { get; }
        }

        internal class InlineSubscription
        {
            public NatsKey Subject { get; }
            public NatsKey QueueGroup { get; }
            public long SubscriptionId { get; }
            public NatsMessageProcess Process { get; }

            public InlineSubscription(NatsKey subject, NatsKey? queueGroup, long subscriptionId, NatsMessageProcess process)
            {
                Subject = subject;
                QueueGroup = queueGroup ?? NatsKey.Empty;
                SubscriptionId = subscriptionId;
                Process = process;
            }



        }

        private NatsStatus _status;
        private readonly CancellationTokenSource _disposeTokenSource;

        private INatsServerPool _serverPool;

        public INatsOptions Options { get; }

        public NatsStatus Status
        {
            get => _status;
            private set
            {
                _logger?.LogTrace("NatsConnection status changed from {Previous} to {Status}", _status, value);

                _status = value;
                StatusChange?.Invoke(this, value);
            }
        }

        public event EventHandler<Exception>? ConnectionException;
        public event EventHandler<NatsStatus>? StatusChange;
        public event EventHandler<NatsInformation>? ConnectionInformation;

        public long SenderQueueSize => _senderQueueSize;
        public long ReceiverQueueSize => _receiverQueueSize;
        public long TransmitBytesTotal => _transmitBytesTotal;
        public long ReceivedBytesTotal => _receivedBytesTotal;
        public long TransmitMessagesTotal => _transmitMessagesTotal;
        public long ReceivedMessagesTotal => _receivedMessagesTotal;

        public NatsConnection()
            : this(new NatsDefaultOptions())
        {
        }

        public NatsConnection(INatsOptions options)
        {
            Options = options;

            _memoryPool = new NatsMemoryPool(options.ArrayPool);

            _disposeTokenSource = new CancellationTokenSource();

            _senderChannel = new NatsPublishChannel(_memoryPool,_disposeTokenSource.Token);    

            _logger = options.LoggerFactory?.CreateLogger<NatsConnection>();

            _serverPool = options.ServerPoolFactory(options);
        }

        public ValueTask ConnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested)
            {
                _logger?.LogError("Connection already disposed");
                throw new ObjectDisposedException("Connection already disposed");
            }

            if (_disconnectSource != null)
            {
                _logger?.LogError("Already connected");
                throw new InvalidAsynchronousStateException("Already connected");
            }

            _disconnectSource = new CancellationTokenSource();

            _senderQueueSize = 0;
            _readWriteAsyncTask = Task.Run(() => ReadWriteAsync(_disconnectSource.Token), _disconnectSource.Token);
            return new ValueTask();
        }

        private async Task ReadWriteAsync(CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting connection loop");

            bool _isRetry = false;

            while (!disconnectToken.IsCancellationRequested)
            {
                Status = NatsStatus.Connecting;

                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;

                _senderChannel.DefaultBufferLength = socket.SendBufferSize;

                _debugDisconnectSource = new CancellationTokenSource();
                using var internalDisconnect = new CancellationTokenSource();
                

                IPEndPoint? ipEndpoint = null;
                try
                {
                    ipEndpoint = await _serverPool.SelectServer(_isRetry);
                    _logger?.LogTrace("Connecting to {Server}", ipEndpoint);                    

                    await socket.ConnectAsync(ipEndpoint);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error connecting to {Server}", ipEndpoint);
                    ConnectionException?.Invoke(this, ex);

                    await Task.Delay(TimeSpan.FromSeconds(1), disconnectToken);

                    _isRetry = true;

                    continue;
                }

                _isRetry = false;
                _receiverQueueSize = 0;

                var readPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0,useSynchronizationContext:false));

                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(disconnectToken, internalDisconnect.Token, _debugDisconnectSource.Token);

                var readTask = Task.Run(() => ReadSocketAsync(socket, readPipe.Writer, combinedCts.Token), combinedCts.Token);
                var processTask = Task.Run(() => ProcessMessagesAsync(readPipe.Reader, combinedCts.Token), combinedCts.Token);
                var writeTask = Task.Run(() => WriteSocketAsync(socket, combinedCts.Token), combinedCts.Token);
               
                try
                {
                    Status = NatsStatus.Connected;
                    _logger?.LogTrace("Socket Connected to {Server}", ipEndpoint);

                    await Task.WhenAny(new[] { readTask, processTask, writeTask });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Exception in the connection loop");
                    
                    if(readTask.Exception is not null)
                        _logger?.LogError(readTask.Exception, "Exception in read task");

                    if(processTask.Exception is not null)
                        _logger?.LogError(processTask.Exception, "Exception in process task");

                    if(writeTask.Exception is not null)
                        _logger?.LogError(writeTask.Exception, "Exception in write task");

                    ConnectionException?.Invoke(this, ex);
                }

                Status = NatsStatus.Connecting;

                if(!internalDisconnect.IsCancellationRequested)
                    internalDisconnect.Cancel();
                
                _logger?.LogTrace("Waiting for read/write/process tasks to finish");

                await WaitAll(readTask, processTask, writeTask);
            }
            _logger?.LogTrace("Exited connection loop");
        }

        private async Task WaitAll(params Task[] tasks)
        {
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Exception in a read/write/process task");

                    ConnectionException?.Invoke(this, ex);
                }
            }
        }

        private async Task ReadSocketAsync(Socket socket, PipeWriter writer, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting ReadSocketAsync loop");

            while (!disconnectToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(socket.ReceiveBufferSize);

                var readBytes = await socket.ReceiveAsync(memory, SocketFlags.None, disconnectToken).ConfigureAwait(false);
                if (readBytes == 0) break;

                writer.Advance(readBytes);
                Interlocked.Add(ref _receiverQueueSize, readBytes);
                Interlocked.Add(ref _receivedBytesTotal, readBytes);


                var flush = await writer.FlushAsync(disconnectToken).ConfigureAwait(false);
                if (flush.IsCompleted || flush.IsCanceled) break;
            }
            _logger?.LogTrace("Exited ReadSocketAsync loop");
        }
                
        private async Task ProcessMessagesAsync(PipeReader reader, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting ProcessMessagesAsync loop");
            var parser = new NatsMessageParser(_memoryPool,_inlineSubscriptions);

            INatsServerMessage[] parsedMessageBuffer = new INatsServerMessage[1024]; //arbitrary

            while (!disconnectToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(disconnectToken).ConfigureAwait(false);
               
                if (read.IsCanceled) break;
                do
                {                    
                    var parsedCount = parser.ParseMessages(read.Buffer, parsedMessageBuffer, out var consumed,out var inlined);

                    reader.AdvanceTo(read.Buffer.GetPosition(consumed));

                    Interlocked.Add(ref _receivedMessagesTotal, inlined);

                    if (consumed == 0) break;

                    Interlocked.Add(ref _receiverQueueSize, (long)-consumed);

                    for (var i = 0; i < parsedCount; i++)
                    {
                        var message = parsedMessageBuffer[i];

                        Interlocked.Increment(ref _receivedMessagesTotal);

                        switch (message)
                        {                            
                            case NatsPing _:
                                await WriteAsync(NatsPong.Instance, disconnectToken);
                                break;

                            case NatsInformation info:
                                _logger?.LogTrace("Received connection information for {Server}, {ConnectionInformation}", info.Host, info);

                                if (Status!= NatsStatus.Connected)
                                {
                                    _logger?.LogTrace("Authenticated Connected to {Server}", info.Host);
                                }

                                NatsInformation = info;

                                _serverPool.SetDiscoveredServers(info.ConnectUrls);

                                _connectionInfoTcs?.TrySetResult(true);

                                ConnectionInformation?.Invoke(this, info);
                                break;
                            
                        }
                    }
                } while (reader.TryRead(out read));
            }
            _logger?.LogTrace("Exited ReadSocketAsync loop");
        }

        private async Task WriteSocketAsync(Socket socket, CancellationToken disconnectToken)
        {
            _logger?.LogTrace("Starting WriteSocketAsync loop");

            ChannelReader<NatsPublishBuffer> reader = _senderChannel.Reader;

            await SendConnect(socket, disconnectToken);
           

            if (_reconnectResendBuffer.Length > 0)
            {
                _logger?.LogTrace("Sending saved reconnect resend buffer");
                try
                {
                    await SocketSend(_reconnectResendBuffer, disconnectToken);
                }
                finally
                {
                    //only retry once
                    _reconnectResendBuffer = ReadOnlyMemory<byte>.Empty;
                }
               

            }

            while (!disconnectToken.IsCancellationRequested)
            {          
                
                await foreach(var chunk in reader.ReadAllAsync(disconnectToken).ConfigureAwait(false))
                {
                    await chunk.Commit();

                    if (chunk.Messages > 0)
                    {
                        var memory = chunk.GetMemory();

                        Interlocked.Add(ref _senderQueueSize, -memory.Length);

                        await SocketSend(memory, disconnectToken);

                        Interlocked.Add(ref _transmitMessagesTotal, chunk.Messages);
                        Interlocked.Add(ref _transmitBytesTotal, memory.Length);
                    }

                    _senderChannel.Return(chunk);
                }
            
            }

            _logger?.LogTrace("Exited WriteSocketAsync loop");

            [MethodImpl(MethodImplOptions.AggressiveInlining)]

            async ValueTask<int> SocketSend(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
            {
                int sent = 0;
                try
                {
                    sent = await socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);

                    //it seems there's no chance sent can be < data length for send success
                    return sent;
                }
                catch (Exception ex) {
                    _logger?.LogError(ex,"Error trying to write to socket");

                    //only save reconnect buffer if nothing transmitted
                    
                    //the risk is having partial messages in the buffer
                    if (sent == 0 && data.Length<= socket.SendBufferSize)
                    {
                        _logger?.LogTrace("Saving reconnect resend buffer");

                        var buffer = new byte[data.Length];
                        data.CopyTo(buffer);
                        _reconnectResendBuffer = buffer;

                        throw new SocketException();
                    }
                    else
                        return sent;
                }
            }
        }

        private async Task SendConnect(Socket socket, CancellationToken disconnectToken)
        {
            var connect = new NatsConnect(Options);
            var connectBuffer = NatsConnect.Serialize(connect);

            var totalSize = connectBuffer.Length;
            List<NatsSub> subs = new();
            foreach (var (id, subscription) in _inlineSubscriptions)
            {
                _logger?.LogTrace("Resubscribing to {Subject} / {QueueGroup} / {SubscriptionId}", subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);

                var sub = new NatsSub(subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId);
                subs.Add(sub);
                totalSize += sub.Length;
            }

            var localConnectionInfoTcs= new TaskCompletionSource<bool>();
            _connectionInfoTcs = localConnectionInfoTcs;

            var timeout = Options.ConnectTimeout;
            if(timeout<=TimeSpan.Zero)
                timeout= TimeSpan.FromSeconds(5);

            _ =Task.Delay(timeout)
                .ContinueWith(t=> localConnectionInfoTcs.TrySetResult(false), TaskContinuationOptions.ExecuteSynchronously);

            //resubscribe
            if (subs.Count > 0)
            {
                var ms = _memoryPool.Rent(totalSize);
                var memory = ms.Memory;
                connectBuffer.CopyTo(ms.Memory);

                memory=memory.Slice(connectBuffer.Length);
                foreach (var sub in subs)
                {
                    sub.Serialize(memory.Span);
                    memory = memory.Slice(sub.Length);
                }

                await socket.SendAsync(ms.Memory, SocketFlags.None, disconnectToken);
            }
            else
            {
                await socket.SendAsync(connectBuffer, SocketFlags.None, disconnectToken);

            }

            await _connectionInfoTcs.Task;
            var connected = _connectionInfoTcs.Task.Result;
            if (!connected)
            {
                throw new TimeoutException("Connect Timeout");
            }

        }

        

        private ValueTask WriteAsync<T>(in T message, CancellationToken cancellationToken) where T: INatsClientMessage
        {
            Interlocked.Add(ref _senderQueueSize, message.Length);

            return _senderChannel.Publish(message, cancellationToken);
        }

        public async ValueTask DisconnectAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) throw new ObjectDisposedException("Connection already disposed");
            try
            {
                _disconnectSource?.Cancel();
                if (_readWriteAsyncTask != null)
                    await _readWriteAsyncTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _disconnectSource?.Dispose();
                _readWriteAsyncTask = null;
                _disconnectSource = null;

                Status = NatsStatus.Disconnected;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposeTokenSource.IsCancellationRequested) return;

            _inlineSubscriptions.Clear();

            await DisconnectAsync();

            _disposeTokenSource.Cancel();
            _disposeTokenSource.Dispose();
        }

        public ValueTask PublishAsync(in NatsKey subject, in NatsPayload? payload = null, in NatsKey? replyTo = null, in NatsMsgHeaders? headers = null, CancellationToken cancellationToken = default)
        {
            if(headers == null)
            {
                return WriteAsync(new NatsPub(subject, replyTo ?? NatsKey.Empty, payload ?? NatsPayload.Empty),cancellationToken);
            }
            else
            {
                return WriteAsync(new NatsHPub(subject, replyTo ?? NatsKey.Empty, headers.Value, payload ?? NatsPayload.Empty), cancellationToken);
            }
        }
              
        internal async ValueTask PublishRaw(byte[] rawData, int position, int messageCount, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var natsBuffer = new NatsPublishBuffer(rawData, position, messageCount);
            natsBuffer.OnCommit += () => tcs.SetResult(true);
            await _senderChannel.Writer.WriteAsync(natsBuffer, cancellationToken);
            await tcs.Task;
            Interlocked.Add(ref _transmitMessagesTotal, messageCount);
        }
                
        private async ValueTask InternalSubscribe(NatsKey subject, NatsMessageProcess process, NatsKey? queueGroup = null, CancellationToken cancellationToken = default)
        {
            var subscription = new InlineSubscription(subject, queueGroup, Interlocked.Increment(ref _nextSubscriptionId), process);

            _inlineSubscriptions.TryAdd(subscription.SubscriptionId, subscription);

            await WriteAsync(new NatsSub(subscription.Subject, subscription.QueueGroup, subscription.SubscriptionId), cancellationToken);
            try
            {
                var tcs = new TaskCompletionSource<bool>();

                cancellationToken.Register(() => tcs.SetResult(true));

                await tcs.Task;
                
            }
            finally
            {
                await WriteAsync(new NatsUnsub(subscription.SubscriptionId, null), CancellationToken.None);
                _inlineSubscriptions.TryRemove(subscription.SubscriptionId,out _);
            }
        }

        public async ValueTask SubscribeAsync(NatsKey subject, NatsMessageProcess process, NatsKey? queueGroup = null, CancellationToken cancellationToken = default)
        {
            await InternalSubscribe(subject, process, queueGroup, cancellationToken);
        }

        public INatsUnsubscriber Subscribe(NatsKey subject, NatsMessageProcess process, NatsKey? queueGroup = null)
        {
            var unsubscriber = new NatsUnsubscriber();

            _= InternalSubscribe(subject, process, queueGroup, unsubscriber.Token);

            return unsubscriber;
        }

        internal void DebugSimulateDisconnection()
        {
            try
            {
                _debugDisconnectSource?.Cancel();
            }
            catch { }
        }
    }

    

}