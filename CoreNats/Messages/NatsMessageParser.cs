﻿namespace CoreNats.Messages
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;

    public class NatsMessageParser
    {
        private readonly NatsMemoryPool _memoryPool;

        private readonly ConcurrentDictionary<long, NatsConnection.InlineSubscription> _inlineSubscriptions;

        public NatsMessageParser() : this(null, null) { }

        internal NatsMessageParser(NatsMemoryPool? memoryPool=null, ConcurrentDictionary<long, NatsConnection.InlineSubscription>? inlineSubscriptions=null)
        {
            _memoryPool = memoryPool ?? new NatsMemoryPool();
            _inlineSubscriptions = inlineSubscriptions ?? new ConcurrentDictionary<long, NatsConnection.InlineSubscription>();
        }


        public int ParseMessages(in ReadOnlySequence<byte> buffer, Span<INatsServerMessage> outMessages, out long consumed,out long inlined)
        {
            inlined = 0;
            var messageCount = 0;
            var reader = new SequenceReader<byte>(buffer);
            while (messageCount<outMessages.Length)
            {
                var previousPosition = reader.Consumed;
                if (!reader.TryReadTo(out ReadOnlySpan<byte> line, (byte) '\n')) break;
                line = line.Slice(0, line.Length - 1); // Slice out \r as well (not just \n)
                
                INatsServerMessage? message = null;
                bool parsed = true;
                switch (line[0])
                {
                    case (byte)'M': parsed = ParseMessageInline(line, ref reader); break;
                    case (byte)'H': parsed = ParseMessageWithHeaderInline(line, ref reader); break;
                    case (byte)'+': message = ParseOk(); break;                    
                    case (byte)'I': message = ParseInformation(line); break;
                    case (byte)'-': message = ParseError(line); break;
                    case (byte)'P': message = line[1] == (byte)'I' ? ParsePing():ParsePong(); break;
                    default:
                        throw new ProtocolViolationException($"Unknown message {Encoding.UTF8.GetString(line)}");
                }

                if (!parsed)
                {
                    // Not enough information to parse the message
                    reader.Rewind(reader.Consumed - previousPosition);
                    break;
                }
                else if(message is not null)
                {
                    outMessages[messageCount] = message;
                    messageCount++;
                }
                else
                {
                    inlined++;
                }
               
            }

            consumed = reader.Consumed;
            return messageCount;
        }

        public bool ParseMessageInline(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            //the idea here is to walk the *line* backwards in sequence just once
            //this should prevent memory fetching as it will usually fit into one cache line
            //it also helps that parsing int is done from right to left
            //we trust nats won't send malformed header lines


            //parse total size
            var multiplier = 1;
            var payloadSize = 0;
            var pointer = line.Length - 1;

            ref byte lineRef = ref MemoryMarshal.GetReference(line);
            byte currentByte = Unsafe.Add(ref MemoryMarshal.GetReference(line), pointer);
            do
            {
                payloadSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            if (reader.Remaining < payloadSize + 2)
                return false; //missing data

            var payloadSizeStart = pointer;
            pointer--;

            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 3)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            //done with first line

            

            long sid = 0;
            pointer = (splitCount == 1) ? payloadSizeStart - 1 : splits[0] - 1;
            //sid            
            multiplier = 1;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                sid += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            if (_inlineSubscriptions.TryGetValue(sid, out var inlineSubscription))
            {
                NatsInlineKey subject;
                NatsInlineKey replyTo;
                ReadOnlySequence<byte> payload = ReadOnlySequence<byte>.Empty;

                if (splitCount == 1)
                {
                    subject = new NatsInlineKey(line.Slice(4, splits[0] - 4));
                    replyTo = NatsInlineKey.Empty();
                }
                else
                {
                    replyTo = new NatsInlineKey(line.Slice(splits[0] + 1, payloadSizeStart - splits[0] - 1));
                    subject = new NatsInlineKey(line.Slice(4, splits[1] - 4));
                }

                if (payloadSize > 0)
                {
                    var payloadSequence = reader.Sequence.Slice(reader.Position, payloadSize);
                    payload = payloadSequence;
                }

                var message = new NatsInlineMsg(ref subject, ref replyTo, sid, payload, NatsMsgHeadersRead.Empty);

                try
                {
                    inlineSubscription.Process.Invoke(ref message);
                }
                catch(Exception ex)
                {
                    //swallow exception
#if DEBUG
                throw;
#endif
                }
            }

            reader.Advance(payloadSize + 2);

            return true;

        }


        public NatsMsg? ParseMessage(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            //the idea here is to walk the *line* backwards in sequence just once
            //this should prevent memory fetching as it will usually fit into one cache line
            //it also helps that parsing int is done from right to left
            //we trust nats won't send malformed header lines


            //parse total size
            var multiplier = 1;
            var payloadSize = 0;
            var pointer = line.Length - 1;

            ref byte lineRef = ref MemoryMarshal.GetReference(line);
            byte currentByte = Unsafe.Add(ref MemoryMarshal.GetReference(line), pointer);
            do
            {
                payloadSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            if (reader.Remaining < payloadSize + 2) return null;

            var payloadSizeStart = pointer;
            pointer--;

            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 3)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            //done with first line



            long sid = 0;
            pointer = (splitCount == 1) ? payloadSizeStart - 1 : splits[0] - 1;
            //sid            
            multiplier = 1;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                sid += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

           
            var wholeMessageSize = payloadSize + line.Length;

#if NET5_0_OR_GREATER
            Memory<byte> copyBuffer = GC.AllocateUninitializedArray<byte>(wholeMessageSize);
#else
            Memory<byte> copyBuffer = new byte[wholeMessageSize];
#endif


            //copy message first line
            var copyMemory = copyBuffer;
            line.CopyTo(copyMemory.Span);

            //copy payload + header
            copyMemory = copyBuffer.Slice(line.Length);
            reader.Sequence.Slice(reader.Position, payloadSize).CopyTo(copyMemory.Span);
            reader.Advance(payloadSize + 2);

            var payload = payloadSize > 0 ? copyMemory.Slice(0, payloadSize) : ReadOnlyMemory<byte>.Empty;

            //get pointers to strings
            NatsKey subject;
            NatsKey replyTo = NatsKey.Empty;


            copyMemory = copyBuffer;
            if (splitCount == 1)
            {
                subject = new NatsKey(copyMemory.Slice(4, splits[0] - 4));
            }
            else
            {
                replyTo = new NatsKey(copyMemory.Slice(splits[0] + 1, payloadSizeStart - splits[0] - 1));

                subject = new NatsKey(copyMemory.Slice(4, splits[1] - 4));
            }

            return new NatsMsg(subject, sid, replyTo, payload);




        }


        public bool ParseMessageWithHeaderInline(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
           
            
            //parse total size
            var multiplier = 1;
            var totalSize = 0;
            var pointer = line.Length - 1;

            ref byte lineRef = ref MemoryMarshal.GetReference(line);
            byte currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                totalSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            

            pointer--;

            //parse header size
            multiplier = 1;
            var headerSize = 0;
            var headerSizeStart = pointer;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                headerSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');


            pointer--;
            var headerSizeEnd = pointer;

            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 4)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            var payloadSize = totalSize - headerSize;
                        
            //sid
            long sid = 0;
            pointer = (splitCount == 1) ? headerSizeEnd : splits[0] - 1;
            multiplier = 1;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                sid += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            //done with first line

            if (reader.Remaining < payloadSize + headerSize + 2) return false;

            if (_inlineSubscriptions.TryGetValue(sid, out var inlineSubscription))
            {
                

                NatsInlineKey subject;
                NatsInlineKey replyTo;
                ReadOnlySequence<byte> payload = ReadOnlySequence<byte>.Empty;
                NatsMsgHeadersRead headers;

                if (splitCount == 1)
                {
                    subject = new NatsInlineKey(line.Slice(5, splits[0] - 5));
                    replyTo = NatsInlineKey.Empty();
                }
                else
                {
                    replyTo = new NatsInlineKey(line.Slice(splits[0] + 1, headerSizeEnd - splits[0]));
                    subject = new NatsInlineKey(line.Slice(5, splits[1] - 5));
                }

                var linePlusHeaderSize = line.Length + 2 + headerSize;

                NatsMemoryOwner headerBuffer = NatsMemoryOwner.Empty;
                var headerSlice = reader.Sequence.Slice(reader.Position, headerSize);
                if (headerSlice.IsSingleSegment)
                {
                    headers = new NatsMsgHeadersRead(headerSlice.First);
                }
                else
                {
                    headerBuffer = _memoryPool.Rent(headerSize);
                    headerSlice.CopyTo(headerBuffer.Memory.Span);
                    headers = new NatsMsgHeadersRead(headerBuffer.Memory);
                }

                if (payloadSize > 0)
                {                    
                    var payloadSequence = reader.Sequence.Slice(reader.Position).Slice(headerSize, payloadSize);
                    payload = payloadSequence;
                }

                var message = new NatsInlineMsg(ref subject, ref replyTo, sid, payload, headers);

                inlineSubscription.Process.Invoke(ref message);

                headerBuffer.Return();

                reader.Advance(headerSize+payloadSize + 2);

                

            }
            //else  //just advance and drop message


            reader.Advance(headerSize + payloadSize + 2);

            return true;


        }


        public NatsMsg? ParseMessageWithHeader(in ReadOnlySpan<byte> line, ref SequenceReader<byte> reader)
        {
            //parse total size
            var multiplier = 1;
            var totalSize = 0;
            var pointer = line.Length - 1;

            ref byte lineRef = ref MemoryMarshal.GetReference(line);
            byte currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                totalSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');



            pointer--;

            //parse header size
            multiplier = 1;
            var headerSize = 0;
            var headerSizeStart = pointer;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                headerSize += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');


            pointer--;
            var headerSizeEnd = pointer;

            Span<int> splits = stackalloc int[3];
            var splitCount = 0;

            while (pointer > 4)
            {
                var ch = line[pointer];
                if (ch == ' ')
                {
                    splits[splitCount] = pointer;
                    splitCount++;

                    if (splitCount > 2)
                        throw new ProtocolViolationException($"Invalid message header {Encoding.UTF8.GetString(line)}");
                }

                pointer--;
            }
            var payloadSize = totalSize - headerSize;

            //sid
            long sid = 0;
            pointer = (splitCount == 1) ? headerSizeEnd : splits[0] - 1;
            multiplier = 1;
            currentByte = Unsafe.Add(ref lineRef, pointer);
            do
            {
                sid += (currentByte - '0') * multiplier;
                multiplier *= 10;
                pointer--;
                currentByte = Unsafe.Add(ref lineRef, pointer);
            } while (currentByte != ' ');

            //done with first line

            if (reader.Remaining < payloadSize + headerSize + 2) 
                return null; //missing data


            var wholeMessageSize = totalSize + line.Length;

#if NET5_0_OR_GREATER
            Memory<byte> copyBuffer = GC.AllocateUninitializedArray<byte>(wholeMessageSize);
#else
            Memory<byte> copyBuffer = new byte[wholeMessageSize];
#endif

            //copy message first line
            var copyMemory = copyBuffer;
            line.CopyTo(copyMemory.Span);

            //copy payload + header
            copyMemory = copyBuffer.Slice(line.Length);
            reader.Sequence.Slice(reader.Position, totalSize).CopyTo(copyMemory.Span);
            reader.Advance(totalSize + 2);

            var headers = copyMemory.Slice(0, headerSize);
            var payload = payloadSize > 0 ? copyMemory.Slice(headerSize, payloadSize) : ReadOnlyMemory<byte>.Empty;

            //get pointers to strings
            NatsKey subject;
            NatsKey replyTo = NatsKey.Empty;

            copyMemory = copyBuffer;
            if (splitCount == 1)
            {
                subject = new NatsKey(copyMemory.Slice(5, splits[0] - 5));
            }
            else
            {
                replyTo = new NatsKey(copyMemory.Slice(splits[0] + 1, headerSizeEnd - splits[0]));
                subject = new NatsKey(copyMemory.Slice(5, splits[1] - 5));
            }

            return new NatsMsg(subject, sid, replyTo, payload, new NatsMsgHeadersRead(headers));


        }


        public NatsOk ParseOk()
        {
            return NatsOk.Instance;
        }

        public NatsInformation? ParseInformation(in ReadOnlySpan<byte> line)
        {
            // Remove "INFO " and parse remainder as JSON
            return JsonSerializer.Deserialize<NatsInformation>(line.Slice(5));
        }

        public NatsError ParseError(in ReadOnlySpan<byte> line)
        {
            if (line.Length == 6) return new NatsError();
            var error = line.Slice(6, line.Length - 9); // Remove "-ERR ''"
            return new NatsError { Error = Encoding.UTF8.GetString(error) };
        }

        public NatsPing ParsePing()
        {
            return NatsPing.Instance;
        }

        public NatsPong ParsePong()
        {
            return NatsPong.Instance;
        }
    }
}