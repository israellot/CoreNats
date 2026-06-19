namespace CoreNats.Tests.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Text;
    using CoreNats;
    using CoreNats.Messages;
    using Xunit;

    /// <summary>
    /// Regression tests for the headerBuffer pool-leak fix in
    /// NatsMessageParser.ParseMessageWithHeaderInline.
    ///
    /// The bug: when the HMSG header spanned multiple buffer segments,
    /// a buffer was rented from _memoryPool and then Process.Invoke was called
    /// with no try/finally, so a throwing handler skipped headerBuffer.Return()
    /// — the pooled buffer leaked permanently.
    ///
    /// The fix: wrap Process.Invoke in try/catch/finally so headerBuffer.Return()
    /// always runs (matching the ParseMessageInline swallow-with-#if-DEBUG-rethrow
    /// pattern, but using finally to guarantee the Return in all paths).
    /// </summary>
    public class NatsMessageParser_HeaderBufferLeakShould
    {
        // ---------------------------------------------------------------
        // Helper: build a ReadOnlySequence<byte> from two separate byte[]
        // segments so that IsSingleSegment == false.
        // ---------------------------------------------------------------
        private static ReadOnlySequence<byte> BuildMultiSegmentSequence(byte[] first, byte[] second)
        {
            var seg1 = new BufferSegment(first);
            var seg2 = seg1.Append(second);
            return new ReadOnlySequence<byte>(seg1, 0, seg2, second.Length);
        }

        private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
        {
            public BufferSegment(byte[] data)
            {
                Memory = data;
            }

            public BufferSegment Append(byte[] data)
            {
                var next = new BufferSegment(data)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }

        // ---------------------------------------------------------------
        // Build the raw bytes that follow the HMSG command line:
        //   <headerBytes><payloadBytes>\r\n
        // We use a minimal valid NATS header: "NATS/1.0\r\n\r\n" (12 bytes)
        // and an empty payload (0 bytes), so the body is just headerBytes + \r\n.
        // ---------------------------------------------------------------
        private static byte[] BuildHmsgBody(string headerText, string payloadText = "")
        {
            // body = header + payload + CRLF terminator
            var header = Encoding.UTF8.GetBytes(headerText);
            var payload = Encoding.UTF8.GetBytes(payloadText);
            var body = new byte[header.Length + payload.Length + 2];
            header.CopyTo(body, 0);
            payload.CopyTo(body, header.Length);
            body[header.Length + payload.Length] = (byte)'\r';
            body[header.Length + payload.Length + 1] = (byte)'\n';
            return body;
        }

        /// <summary>
        /// When the handler throws and the header spans multiple segments,
        /// the pooled headerBuffer must still be returned.
        ///
        /// Observability: we inject a custom ArrayPool that counts Return()
        /// calls, then verify count == 1 after parsing.
        /// </summary>
        [Fact]
        public void ReturnPooledHeaderBufferWhenHandlerThrows_MultiSegmentHeader()
        {
            // Arrange — a trackable ArrayPool
            var returnCount = 0;
            var rentCount = 0;
            var trackingPool = new TrackingArrayPool(onReturn: () => returnCount++, onRent: () => rentCount++);
            var memoryPool = new NatsMemoryPool(trackingPool);

            // SID = 42; handler always throws
            const long sid = 42;
            var inlineSubscriptions = new ConcurrentDictionary<long, NatsConnection.InlineSubscription>();
            inlineSubscriptions[sid] = new NatsConnection.InlineSubscription(
                subject: new NatsKey("FOO.BAR"),
                queueGroup: null,
                subscriptionId: sid,
                process: new NatsMessageInlineProcess((ref NatsInlineMsg msg) =>
                {
                    throw new InvalidOperationException("handler intentionally throws");
                }));

            var parser = new NatsMessageParser(memoryPool, inlineSubscriptions);

            // Build HMSG body: header = "NATS/1.0\r\n\r\n" (12 bytes), payload = "" (0 bytes)
            // HMSG line: "HMSG FOO.BAR 42 12 12"  (totalSize == headerSize since payload is 0)
            const string headerText = "NATS/1.0\r\n\r\n"; // 12 bytes
            var headerBytes = Encoding.UTF8.GetBytes(headerText);
            var headerSize = headerBytes.Length; // 12
            var totalSize = headerSize;          // no payload

            // Build the body bytes: headerBytes + \r\n
            var bodyBytes = new byte[headerBytes.Length + 2];
            Array.Copy(headerBytes, bodyBytes, headerBytes.Length);
            bodyBytes[headerBytes.Length] = (byte)'\r';
            bodyBytes[headerBytes.Length + 1] = (byte)'\n';

            // Split the header across two segments to force IsSingleSegment == false.
            // Segment 1: first 6 bytes of header. Segment 2: remaining 6 + the trailing \r\n.
            int splitAt = 6; // mid-point of the 12-byte header
            var seg1Bytes = new byte[splitAt];
            var seg2Bytes = new byte[bodyBytes.Length - splitAt];
            Array.Copy(bodyBytes, 0, seg1Bytes, 0, splitAt);
            Array.Copy(bodyBytes, splitAt, seg2Bytes, 0, seg2Bytes.Length);

            var multiSegmentBody = BuildMultiSegmentSequence(seg1Bytes, seg2Bytes);

            // Verify that the slice really is multi-segment (guard the test assumption)
            var sliceForCheck = multiSegmentBody.Slice(0, headerSize);
            Assert.False(sliceForCheck.IsSingleSegment,
                "Test setup: headerSlice must be multi-segment to exercise the rented-buffer path.");

            var reader = new SequenceReader<byte>(multiSegmentBody);

            // HMSG command line (no \r\n — the parser reads the line separately)
            var line = Encoding.UTF8.GetBytes($"HMSG FOO.BAR {sid} {headerSize} {totalSize}");

            // Act — should NOT throw in release mode; the handler exception is swallowed
            // (matching ParseMessageInline behavior). In DEBUG builds it would rethrow —
            // we wrap in try/catch to make the test pass in both configurations.
            try
            {
                parser.ParseMessageWithHeaderInline(line, ref reader);
            }
            catch (InvalidOperationException)
            {
                // Only reached in DEBUG builds where handler exceptions are re-thrown.
                // The pool Return() must still have been called (finally guarantees it).
            }

            // Assert — headerBuffer was rented and then returned exactly once
            Console.WriteLine($"[DEBUG] rentCount={rentCount}, returnCount={returnCount}, IsSingleSegment={sliceForCheck.IsSingleSegment}");
            Assert.True(rentCount >= 1, $"Expected at least 1 pool Rent, got {rentCount}. IsSingleSegment={sliceForCheck.IsSingleSegment}");
            Assert.Equal(1, returnCount);
        }

        /// <summary>
        /// Baseline: when the header fits in a single segment, the buffer is NatsMemoryOwner.Empty
        /// and Return() is a no-op (trackingPool.Return() must NOT be called).
        /// The handler still throws; parser must not crash.
        /// </summary>
        [Fact]
        public void DoesNotRentBufferWhenHeaderIsSingleSegment_HandlerThrows()
        {
            var rentCount = 0;
            var trackingPool = new TrackingArrayPool(onReturn: null, onRent: () => rentCount++);
            var memoryPool = new NatsMemoryPool(trackingPool);

            const long sid = 7;
            var inlineSubscriptions = new ConcurrentDictionary<long, NatsConnection.InlineSubscription>();
            inlineSubscriptions[sid] = new NatsConnection.InlineSubscription(
                subject: new NatsKey("FOO"),
                queueGroup: null,
                subscriptionId: sid,
                process: new NatsMessageInlineProcess((ref NatsInlineMsg msg) =>
                {
                    throw new InvalidOperationException("still throws");
                }));

            var parser = new NatsMessageParser(memoryPool, inlineSubscriptions);

            const string headerText = "NATS/1.0\r\n\r\n"; // 12 bytes
            var headerBytes = Encoding.UTF8.GetBytes(headerText);
            int headerSize = headerBytes.Length;

            var bodyBytes = new byte[headerBytes.Length + 2];
            Array.Copy(headerBytes, bodyBytes, headerBytes.Length);
            bodyBytes[headerBytes.Length] = (byte)'\r';
            bodyBytes[headerBytes.Length + 1] = (byte)'\n';

            // Single contiguous segment — IsSingleSegment will be true
            var singleSegmentSeq = new ReadOnlySequence<byte>(bodyBytes);
            var reader = new SequenceReader<byte>(singleSegmentSeq);

            var line = Encoding.UTF8.GetBytes($"HMSG FOO {sid} {headerSize} {headerSize}");

            try
            {
                parser.ParseMessageWithHeaderInline(line, ref reader);
            }
            catch (InvalidOperationException) { /* DEBUG rethrow */ }

            // No buffer should have been rented from the pool
            Assert.Equal(0, rentCount);
        }
    }

    // ---------------------------------------------------------------
    // A minimal ArrayPool<byte> wrapper that counts Rent/Return calls.
    // ---------------------------------------------------------------
    internal sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly ArrayPool<byte> _inner = ArrayPool<byte>.Shared;
        private readonly Action? _onReturn;
        private readonly Action? _onRent;

        public TrackingArrayPool(Action? onReturn = null, Action? onRent = null)
        {
            _onReturn = onReturn;
            _onRent = onRent;
        }

        public override byte[] Rent(int minimumLength)
        {
            _onRent?.Invoke();
            return _inner.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            _onReturn?.Invoke();
            _inner.Return(array, clearArray);
        }
    }
}
