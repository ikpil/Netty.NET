/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.IO.MemoryMappedFiles;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

/**
 * An abstract test class for channel buffers
 */
public abstract class AbstractByteBufTest : IDisposable
{
    [Fact]
    public void GetByteArrayBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBytes(-1, Array.Empty<byte>());
        });
    }

    [Fact]
    public void GetByteArrayBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBytes(-1, Array.Empty<byte>().AsSpan(0, 0));
        });
    }

    [Fact]
    public void GetByteArrayBoundaryCheck3()
    {
        byte[] dst = new byte[4];
        buffer.SetInt(0, 0x01020304);
        // CLR: AsSpan rejects this array range before GetBytes can be invoked.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, dst.AsSpan(-1, 4)));

        // No partial copy is expected.
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(0, dst[2]);
        Assert.Equal(0, dst[3]);
    }

    [Fact]
    public void GetByteArrayBoundaryCheck4()
    {
        byte[] dst = new byte[4];
        buffer.SetInt(0, 0x01020304);
        // CLR: AsSpan rejects this array range before GetBytes can be invoked.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, dst.AsSpan(1, 4)));

        // No partial copy is expected.
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(0, dst[2]);
        Assert.Equal(0, dst[3]);
    }

    [Fact]
    public void TestByteArrayTransfer()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value.AsSpan(random.Next(BLOCK_SIZE), BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestRandomByteArrayTransfer1()
    {
        byte[] value = new byte[BLOCK_SIZE];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            buffer.GetBytes(i, value);

            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value[j]);
            }
        }
    }

    [Fact]
    public void TestRandomByteArrayTransfer2()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value.AsSpan(random.Next(BLOCK_SIZE), BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value[j]);
            }
        }
    }

    [Fact]
    public void TestSequentialByteArrayTransfer1()
    {
        byte[] value = new byte[BLOCK_SIZE];
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value);

            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestSequentialByteArrayTransfer2()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            buffer.WriteBytes(value.AsSpan(readerIndex, BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value.AsSpan(valueOffset, BLOCK_SIZE));

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8]);
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8]);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8]);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8]);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestSliceCapacityChange()
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            AssertSliceCapacityChange(false);
        });
    }

    [Fact]
    public void TestRetainedSliceCapacityChange()
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            AssertSliceCapacityChange(true);
        });
    }

    [Fact]
    public void TestReaderIndexLargerThanWriterIndex()
    {
        string content1 = "hello";
        string content2 = "world";
        int length = content1.Length + content2.Length;
        ByteBuf buffer = NewBuffer(length);
        buffer.SetIndex(0, 0);
        buffer.WriteCharSequence(content1, Encoding.ASCII);
        buffer.MarkWriterIndex();
        buffer.SkipBytes(content1.Length);
        buffer.WriteCharSequence(content2, Encoding.ASCII);
        buffer.SkipBytes(content2.Length);
        Assert.True(buffer.ReaderIndex <= buffer.WriterIndex);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.ResetWriterIndex();
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityEnforceMaxCapacity()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.Capacity = 14;
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityNegative()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.Capacity = -1;
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityDecrease()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);

        try
        {
            buffer.Capacity = 2;
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(13, buffer.MaxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityIncrease()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);

        try
        {
            buffer.Capacity = 4;
            Assert.Equal(4, buffer.Capacity);
            Assert.Equal(13, buffer.MaxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalMaxFastWritableBytesTracksWrittenBytes()
    {
        ByteBuf buf = NewBuffer(4, 10);
        int max = buf.MaxFastWritableBytes;
        buf.WriteByte(1);
        Assert.Equal(max - 1, buf.MaxFastWritableBytes);
        buf.Release();
    }

    protected void AssertOriginalMaxFastWritableBytes()
    {
        ByteBuf buffer = NewBuffer(150, 500);
        buffer.WriterIndex = 100;
        Assert.Equal(50, buffer.WritableBytes);
        Assert.Equal(150, buffer.Capacity);
        Assert.Equal(500, buffer.MaxCapacity);
        Assert.Equal(400, buffer.MaxWritableBytes);
        // Default implementation has fast writable == writable
        Assert.Equal(50, buffer.MaxFastWritableBytes);
        buffer.Release();
    }

    private void AssertSliceCapacityChange(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf slice = retainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 1, 3)
                                      : buf.Slice(buf.ReaderIndex + 1, 3);

        try
        {
            slice.Capacity = 10;
        }
        finally
        {
            if (retainedSlice)
            {
                slice.Release();
            }

            buf.Release();
        }
    }

    [Fact]
    public virtual void TestDuplicateReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            AssertReadGatheringByteChannelMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public virtual void TestSliceReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            AssertReadGatheringByteChannelMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    private static void AssertReadGatheringByteChannelMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
    {
        Assert.Equal(buffer.ReadableBytes, expectedBytes.Length);
        int remaining = 60000;
        var errors = new ConcurrentQueue<Exception>();
        using var completed = new ManualResetEventSlim();
        using var barrier = new Barrier(11);
        var threads = new List<Thread>();
        var clock = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    // One private TCP connection per worker avoids sharing channel state.
                    // Every iteration still captures a fresh complete output array.
                    using var channel = new ConcurrentGatheringChannel();

                    while (Volatile.Read(ref remaining) > 0)
                    {
                        ByteBuf buf;

                        if (slice)
                        {
                            buf = buffer.Slice();
                        }
                        else
                        {
                            buf = buffer.Duplicate();
                        }

                        while (buf.IsReadable())
                        {
                            if (clock.Elapsed >= TimeSpan.FromSeconds(30))
                                throw new TimeoutException("The original concurrent channel timeout expired.");

                            // Never happens
                            // CLR: unexpected I/O errors reach the outer worker catch immediately.
                            buf.ReadBytes(channel.Socket, buf.ReadableBytes);
                        }

                        Assert.Equal(expectedBytes, channel.WrittenBytes(expectedBytes.Length));

                        if (Interlocked.Decrement(ref remaining) <= 0)
                        {
                            completed.Set();
                        }
                    }

                    channel.AssertNoTrailingBytes();
                }
                catch (Exception cause)
                {
                    errors.Enqueue(cause);
                    // Short-circuit: wake up the main thread immediately
                    Interlocked.Exchange(ref remaining, 0);
                    completed.Set();
                }
                finally
                {
                    try
                    {
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    }
                    catch (Exception cause)
                    {
                        // ignore
                        // CLR: preserve coordinator failures as well as the original worker failure.
                        errors.Enqueue(cause);
                    }
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        try
        {
            Assert.True(completed.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        }
        catch (Exception cause)
        {
            errors.Enqueue(cause);
            Interlocked.Exchange(ref remaining, 0);
        }
        finally
        {
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
        }

        // Ensure failures in worker threads are propagated to the test thread.
        if (!errors.IsEmpty)
        {
            throw new AggregateException("Worker thread failed", errors);
        }
    }

    private sealed class ConcurrentGatheringChannel : IDisposable
    {
        internal Socket Socket { get; }
        private readonly Socket _peer;

        internal ConcurrentGatheringChannel()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                Socket.NoDelay = true;
                Socket.SendTimeout = 5000;
                Socket.Connect(listener.LocalEndPoint);
                _peer = listener.Accept();
                _peer.ReceiveTimeout = 5000;
            }
            catch
            {
                _peer?.Dispose();
                Socket.Dispose();
                throw;
            }
        }

        internal byte[] WrittenBytes(int length)
        {
            byte[] bytes = new byte[length];
            int offset = 0;

            while (offset < bytes.Length)
            {
                int count = _peer.Receive(bytes.AsSpan(offset), SocketFlags.None);
                Assert.True(count > 0, "The channel closed before a complete output was received.");
                offset += count;
            }

            return bytes;
        }

        internal void AssertNoTrailingBytes()
        {
            // Reusing TCP must not hide an over-send, including whole repeated eight-byte payloads.
            Socket.Shutdown(SocketShutdown.Send);
            Span<byte> extra = stackalloc byte[1];
            Assert.Equal(0, _peer.Receive(extra, SocketFlags.None));
        }

        public void Dispose()
        {
            _peer.Dispose();
            Socket.Dispose();
        }
    }

    public enum CharSequenceType { String, AsciiString }

    [Theory]
    [InlineData("UTF-8", CharSequenceType.String)]
    [InlineData("UTF-8", CharSequenceType.AsciiString)]
    [InlineData("US-ASCII", CharSequenceType.String)]
    [InlineData("US-ASCII", CharSequenceType.AsciiString)]
    [InlineData("ISO-8859-1", CharSequenceType.String)]
    [InlineData("ISO-8859-1", CharSequenceType.AsciiString)]
    public async Task TestSetCharSequenceMultipleThreads(string charset, CharSequenceType charSeqType)
    {
        // Java maxBytesPerChar is per UTF-16 unit. Encoding.GetMaxByteCount includes
        // fallback/flush overhead, so preserve the original 10/32/32-character inputs explicitly.
        (Encoding encoding, int maxBytesPerChar) = charset switch
        {
            "UTF-8" => (Encoding.UTF8, 3),
            "US-ASCII" => (Encoding.ASCII, 1),
            "ISO-8859-1" => (Encoding.Latin1, 1),
            _ => throw new ArgumentException("Unknown original charset", nameof(charset))
        };
        int bufSize = 32;
        ByteBuf[] bufs = new ByteBuf[16];
        int iterations = 256;
        using var start = new SemaphoreSlim(0);
        using var finish = new SemaphoreSlim(0);
        var tasks = new List<Task>();
        // CLR: the supplemental index check compares with each factory's initial indices (a slice factory
        // returns a full writer index).
        int[] readerIndices = new int[bufs.Length], writerIndices = new int[bufs.Length];
        Exception failure = null;

        try
        {
            for (int i = 0; i < bufs.Length; i++)
            {
                bufs[i] = NewBuffer(bufSize);
                // Make the supplemental content assertion independent of stale pooled allocation contents.
                bufs[i].SetZero(0, bufSize);
                readerIndices[i] = bufs[i].ReaderIndex;
                writerIndices[i] = bufs[i].WriterIndex;
            }

            char[] cs = new char[bufSize / maxBytesPerChar];
            Array.Fill(cs, 'a');
            string text = charSeqType switch
            {
                CharSequenceType.String => new string(cs),
                CharSequenceType.AsciiString => new AsciiString(cs).ToString(),
                _ => throw new ArgumentException("Unknown original sequence type", nameof(charSeqType))
            };

            for (int i = 0; i < bufs.Length; i++)
            {
                ByteBuf buf = bufs[i];
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    finish.Release();
                    start.Wait();

                    for (int j = 0; j < iterations; j++)
                    {
                        buf.SetCharSequence(0, text, encoding);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }

            CancellationToken token = TestContext.Current.CancellationToken;

            for (int i = 0; i < bufs.Length; i++)
            {
                await finish.WaitAsync(token);
            }

            start.Release(bufs.Length);
            Task all = Task.WhenAll(tasks);

            try
            {
                await all.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Propagate interrupted exceptions immediately.
                // CLR cancellation propagates after the finally block safely drains borrowed workers.
                throw;
            }
            catch
            {
                throw new AggregateException("Worker threads failed", all.Exception.InnerExceptions);
            }

            // Original success means all Futures completed. Also verify actual bytes and absolute indices.
            byte[] expected = new byte[cs.Length];
            Array.Fill(expected, (byte)'a');

            for (int i = 0; i < bufs.Length; i++)
            {
                ByteBuf buf = bufs[i];
                Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 0, expected.Length));
                Assert.Equal(readerIndices[i], buf.ReaderIndex);
                Assert.Equal(writerIndices[i], buf.WriterIndex);
            }
        }
        catch (Exception cause)
        {
            failure = cause;
            throw;
        }
        finally
        {
            start.Release(bufs.Length);

            try
            {
                await Task.WhenAll(tasks);
            }
            catch when (failure != null)
            {
            }
            finally
            {
                foreach (ByteBuf buf in bufs)
                {
                    buf?.Release();
                }
            }
        }
    }

    [Fact]
    public void TestToStringMultipleThreads()
    {
        buffer.Clear();
        buffer.WriteBytes(Encoding.Latin1.GetBytes("Hello, World!"));
        AssertToStringMultipleThreads(buffer);
    }

    private static void AssertToStringMultipleThreads(ByteBuf buffer)
    {
        string expected = buffer.ToString(Encoding.Latin1);
        using var startBarrier = new Barrier(10);
        var errors = new ConcurrentQueue<Exception>();
        var threads = new List<Thread>();

        for (int i = 0; i < 10; i++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                    int counter = 3000;

                    while (errors.IsEmpty && counter-- > 0)
                        Assert.Equal(expected, buffer.ToString(Encoding.Latin1));
                }
                catch (Exception cause)
                {
                    errors.Enqueue(cause);
                }
            });
            threads.Add(thread);
        }

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        JoinAllAndReportErrors(threads, errors, TimeSpan.FromSeconds(30));
    }

    private static void JoinAllAndReportErrors(List<Thread> threads,
        ConcurrentQueue<Exception> errors, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        try
        {
            foreach (Thread thread in threads)
            {
                TimeSpan remaining = timeout - clock.Elapsed;
                if (remaining <= TimeSpan.Zero || !thread.Join(remaining))
                    throw new TimeoutException("Worker threads did not finish within the original test timeout.");
            }
        }
        catch (Exception cause)
        {
            errors.Enqueue(cause);

            // CLR: interruption wakes managed waits; publishing the error also stops the read loops.
            foreach (Thread thread in threads)
                if (thread.IsAlive)
                {
                    thread.Interrupt();
                }
        }
        finally
        {
            // Workers must relinquish borrowed storage before fixture cleanup releases its owner.
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
        }

        if (!errors.IsEmpty)
        {
            throw new AggregateException(errors);
        }
    }

    [Fact]
    public void TestCopyMultipleThreads0()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        buffer.Clear();
        buffer.WriteBytes(bytes);
        AssertCopyMultipleThreads(buffer);
    }

    private static void AssertCopyMultipleThreads(ByteBuf buffer)
    {
        ByteBuf expected = buffer.Copy();

        try
        {
            using var startBarrier = new Barrier(10);
            var errors = new ConcurrentQueue<Exception>();
            var threads = new List<Thread>();

            for (int i = 0; i < 10; i++)
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        Assert.True(startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                        int counter = 3000;

                        while (errors.IsEmpty && counter-- > 0)
                        {
                            ByteBuf copy = buffer.Copy();

                            try
                            {
                                Assert.Equal(expected, copy);
                            }
                            finally
                            {
                                copy.Release();
                            }
                        }
                    }
                    catch (Exception cause)
                    {
                        errors.Enqueue(cause);
                    }
                });
                threads.Add(thread);
            }

            foreach (Thread thread in threads)
            {
                thread.Start();
            }

            JoinAllAndReportErrors(threads, errors, TimeSpan.FromSeconds(60));
        }
        finally
        {
            expected.Release();
        }
    }

    [Fact]
    public virtual void TestDuplicateReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            AssertReadOutputStreamMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public virtual void TestSliceReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            AssertReadOutputStreamMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    private static void AssertReadOutputStreamMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
    {
        int remaining = 60000;
        var errors = new ConcurrentQueue<Exception>();
        using var completed = new ManualResetEventSlim();
        using var barrier = new Barrier(11);
        var threads = new List<Thread>();

        for (int i = 0; i < 10; i++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    while (Volatile.Read(ref remaining) > 0)
                    {
                        ByteBuf buf;

                        if (slice)
                        {
                            buf = buffer.Slice();
                        }
                        else
                        {
                            buf = buffer.Duplicate();
                        }

                        using var output = new MemoryStream();

                        while (buf.IsReadable())
                        {
                            // Never happens
                            // CLR: an unexpected IOException is reported by the outer worker catch.
                            buf.ReadBytes(output, buf.ReadableBytes);
                        }

                        Assert.Equal(expectedBytes, output.ToArray());

                        // CountDownLatch saturates at zero; at most nine already-started iterations may finish later.
                        if (Interlocked.Decrement(ref remaining) <= 0)
                        {
                            completed.Set();
                        }
                    }
                }
                catch (Exception cause)
                {
                    errors.Enqueue(cause);
                    // Short-circuit: wake up the main thread immediately
                    Interlocked.Exchange(ref remaining, 0);
                    completed.Set();
                }
                finally
                {
                    try
                    {
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    }
                    catch (Exception cause)
                    {
                        // ignore
                        // CLR: retain barrier failures as well so no worker exception is lost.
                        errors.Enqueue(cause);
                    }
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        try
        {
            Assert.True(completed.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        }
        catch (Exception cause)
        {
            errors.Enqueue(cause);
            Interlocked.Exchange(ref remaining, 0);
        }
        finally
        {
            foreach (Thread thread in threads)
            {
                thread.Join();
            }
        }

        // Ensure failures in worker threads are propagated to the test thread.
        if (!errors.IsEmpty)
        {
            throw new AggregateException("Worker thread failed", errors);
        }
    }

    [Fact]
    public virtual async Task TestDuplicateBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            await AssertBytesInArrayMultipleThreads(buffer, bytes, false);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public virtual async Task TestSliceBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);

        try
        {
            await AssertBytesInArrayMultipleThreads(buffer, bytes, true);
        }
        finally
        {
            buffer.Release();
        }
    }

    private static async Task AssertBytesInArrayMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
    {
        using var startBarrier = new Barrier(10);
        using var endBarrier = new Barrier(11);
        Action callable = () =>
        {
            startBarrier.SignalAndWait();

            try
            {
                for (int i = 0; i < 6000; i++)
                {
                    ByteBuf buf;

                    if (slice)
                    {
                        buf = buffer.Slice();
                    }
                    else
                    {
                        buf = buffer.Duplicate();
                    }

                    byte[] array = new byte[8];
                    buf.ReadBytes(array);
                    Assert.Equal(expectedBytes, array);
                    Array.Fill(array, (byte)0);
                    buf.GetBytes(0, array);
                    Assert.Equal(expectedBytes, array);
                }
            }
            finally
            {
                endBarrier.SignalAndWait();
            }
        };
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            // FutureTask plus a dedicated Thread maps to a LongRunning Task on the default scheduler.
            tasks.Add(Task.Factory.StartNew(callable, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }

        try
        {
            Assert.True(endBarrier.SignalAndWait(TimeSpan.FromSeconds(30)));

            foreach (Task task in tasks)
            {
                await task.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            // Unlike interrupting a JVM thread, CLR cancellation cannot abort a storage access.
            // Observe every Future and finish all workers before releasing the shared owner.
            await Task.WhenAll(tasks);
        }
    }

    [Fact]
    public void TestReadBytesAndWriteBytesWithFileChannel()
    {
        string file = Path.GetTempFileName();

        try
        {
            using var channel = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
            // channelPosition should never be changed
            long channelPosition = channel.Position;
            byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d' };
            int len = bytes.Length;
            ByteBuf buffer = NewBuffer(len);
            ByteBuf buffer2 = null;

            try
            {
                buffer.ResetReaderIndex();
                buffer.ResetWriterIndex();
                buffer.WriteBytes(bytes);
                int oldReaderIndex = buffer.ReaderIndex;
                Assert.Equal(len, buffer.ReadBytes(channel.SafeFileHandle, 10, len));
                Assert.Equal(oldReaderIndex + len, buffer.ReaderIndex);
                Assert.Equal(channelPosition, channel.Position);
                buffer2 = NewBuffer(len);
                buffer2.ResetReaderIndex();
                buffer2.ResetWriterIndex();
                int oldWriterIndex = buffer2.WriterIndex;
                Assert.Equal(len, buffer2.WriteBytes(channel.SafeFileHandle, 10, len));
                Assert.Equal(channelPosition, channel.Position);
                Assert.Equal(oldWriterIndex + len, buffer2.WriterIndex);
                Assert.Equal((byte)'a', buffer2.GetByte(0));
                Assert.Equal((byte)'b', buffer2.GetByte(1));
                Assert.Equal((byte)'c', buffer2.GetByte(2));
                Assert.Equal((byte)'d', buffer2.GetByte(3));
            }
            finally
            {
                buffer.Release();
                buffer2?.Release();
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TestGetBytesAndSetBytesWithFileChannel()
    {
        string file = Path.GetTempFileName();

        try
        {
            using var channel = new FileStream(file, FileMode.Open, FileAccess.ReadWrite);
            // channelPosition should never be changed
            long channelPosition = channel.Position;
            byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d' };
            int len = bytes.Length;
            ByteBuf buffer = NewBuffer(len);
            ByteBuf buffer2 = null;

            try
            {
                buffer.ResetReaderIndex();
                buffer.ResetWriterIndex();
                buffer.WriteBytes(bytes);
                int oldReaderIndex = buffer.ReaderIndex;
                Assert.Equal(len, buffer.GetBytes(oldReaderIndex, channel.SafeFileHandle, 10, len));
                Assert.Equal(oldReaderIndex, buffer.ReaderIndex);
                Assert.Equal(channelPosition, channel.Position);
                buffer2 = NewBuffer(len);
                buffer2.ResetReaderIndex();
                buffer2.ResetWriterIndex();
                int oldWriterIndex = buffer2.WriterIndex;
                Assert.Equal(buffer2.SetBytes(oldWriterIndex, channel.SafeFileHandle, 10, len), len);
                Assert.Equal(channelPosition, channel.Position);
                Assert.Equal(oldWriterIndex, buffer2.WriterIndex);
                Assert.Equal((byte)'a', buffer2.GetByte(oldWriterIndex));
                Assert.Equal((byte)'b', buffer2.GetByte(oldWriterIndex + 1));
                Assert.Equal((byte)'c', buffer2.GetByte(oldWriterIndex + 2));
                Assert.Equal((byte)'d', buffer2.GetByte(oldWriterIndex + 3));
            }
            finally
            {
                buffer.Release();
                buffer2?.Release();
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    private const int CAPACITY = 4096; // Must be even
    private const int BLOCK_SIZE = 128;
    private const int JAVA_BYTEBUFFER_CONSISTENCY_ITERATIONS = 100;
    private int seed;
    private Random random;
    private ByteBuf buffer;
    private readonly List<ByteBuf> _peers = new();
    protected readonly NativeMemoryAllocator Native = new();
    protected IByteBufAllocator Allocator;
    private UnpooledByteBufAllocator _peerAllocator;

    protected ByteBuf NewBuffer(int capacity)
    {
        return NewBuffer(capacity, int.MaxValue);
    }

    protected abstract ByteBuf NewBuffer(int capacity, int maxCapacity);

    protected void Init(IByteBufAllocator allocator)
    {
        Allocator = allocator;
        _peerAllocator = new UnpooledByteBufAllocator(false, Native);
        buffer = NewBuffer(CAPACITY);
        seed = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        random = new Random(seed);
    }

    // Unpooled peer storage is independent of the fixture's actual root allocator.
    private ByteBuf WrappedBuffer(byte[] bytes)
    {
        return Own(Unpooled.WrappedBuffer(bytes));
    }

    private ByteBuf WrappedMemory(Memory<byte> bytes)
    {
        return Own(Unpooled.WrappedBuffer(bytes));
    }

    private ByteBuf DirectBuffer(int capacity)
    {
        return Own(_peerAllocator.DirectBuffer(capacity));
    }

    private ByteBuf Own(ByteBuf value)
    {
        _peers.Add(value);
        return value;
    }

    public virtual void Dispose()
    {
        try
        {
            if (buffer != null)
            {
                Assert.True(buffer.Release());
                Assert.Equal(0, buffer.RefCnt);

                try
                {
                    buffer.Release();
                }
                catch (Exception)
                {
                    // Ignore.
                }

                buffer = null;
            }
        }
        finally
        {
            // Heap peers omitted by the Java GC-owned fixture also have CLR reference-counted facades.
            foreach (ByteBuf peer in _peers)
                if (peer.RefCnt > 0)
                    peer.Release();

            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    [Fact]
    public void TestRandomHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE];
        ByteBuf value = WrappedBuffer(valueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetIndex(0, BLOCK_SIZE);
            buffer.SetBytes(i, value);
            Assert.Equal(BLOCK_SIZE, value.ReaderIndex);
            Assert.Equal(BLOCK_SIZE, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.Clear();
            buffer.GetBytes(i, value);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(BLOCK_SIZE, value.WriterIndex);

            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }

    }

    [Fact]
    public void TestRandomHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            buffer.SetBytes(i, value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value, valueOffset, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }

    }

    [Fact]
    public void TestRandomDirectBufferTransfer()
    {
        byte[] tmp = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(tmp);
            value.SetBytes(0, tmp.AsSpan(0, value.Capacity));
            buffer.SetBytes(i, value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
        }

        random = new Random(seed);
        ByteBuf expectedValue = DirectBuffer(BLOCK_SIZE * 2);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(tmp);
            expectedValue.SetBytes(0, tmp.AsSpan(0, expectedValue.Capacity));
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value, valueOffset, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }

        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(valueContent.Length, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(valueContent.Length, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = readerIndex;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialDirectBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            value.SetBytes(0, valueContent);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialDirectBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = 0;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            value.ReaderIndex = readerIndex;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.SetBytes(0, valueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }

        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialByteBufferBackedHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedMemory(new byte[BLOCK_SIZE * 2].AsMemory());
        value.WriterIndex = 0;
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            value.SetBytes(0, valueContent);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialByteBufferBackedHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedMemory(new byte[BLOCK_SIZE * 2].AsMemory());
        value.WriterIndex = 0;
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = 0;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            value.ReaderIndex = readerIndex;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.SetBytes(0, valueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }

            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialCopiedBufferTransfer1()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            byte[] value = new byte[BLOCK_SIZE];
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            ByteBuf actualValue = buffer.ReadBytes(BLOCK_SIZE);
            Assert.Equal(WrappedBuffer(expectedValue), actualValue);

            // Make sure if it is a copied buffer.
            actualValue.SetByte(0, unchecked((byte)(actualValue.GetByte(0) + 1)));
            Assert.False(buffer.GetByte(i) == actualValue.GetByte(0));
            actualValue.Release();
        }

    }

    [Fact]
    public void TestStreamTransfer1()
    {
        byte[] expected = new byte[buffer.Capacity];
        random.NextBytes(expected);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            using var input = new MemoryStream(expected, i, BLOCK_SIZE, false);
            Assert.Equal(BLOCK_SIZE, buffer.SetBytes(i, input, BLOCK_SIZE));
            // CLR zero-length reads return zero; verify the original EOF condition independently.
            Assert.Equal(0, buffer.SetBytes(i, input, 0));
            Assert.Equal(-1, input.ReadByte());
        }

        using var output = new MemoryStream();

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            buffer.GetBytes(i, output, BLOCK_SIZE);
        }

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void TestStreamTransfer2()
    {
        byte[] expected = new byte[buffer.Capacity];
        random.NextBytes(expected);
        buffer.Clear();

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            using var input = new MemoryStream(expected, i, BLOCK_SIZE, false);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(input, BLOCK_SIZE);
            Assert.Equal(i + BLOCK_SIZE, buffer.WriterIndex);
        }

        using var output = new MemoryStream();

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            Assert.Equal(i, buffer.ReaderIndex);
            buffer.ReadBytes(output, BLOCK_SIZE);
            Assert.Equal(i + BLOCK_SIZE, buffer.ReaderIndex);
        }

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void TestShortConsistentWithByteBuffer()
    {
        AssertShortConsistentWithByteBuffer(true, true);
        AssertShortConsistentWithByteBuffer(true, false);
        AssertShortConsistentWithByteBuffer(false, true);
        AssertShortConsistentWithByteBuffer(false, false);

    }

    [Fact]
    public void TestMediumConsistentWithByteBuffer()
    {
        AssertMediumConsistentWithByteBuffer(true, true);
        AssertMediumConsistentWithByteBuffer(true, false);
        AssertMediumConsistentWithByteBuffer(false, true);
        AssertMediumConsistentWithByteBuffer(false, false);

    }

    [Fact]
    public void TestIntConsistentWithByteBuffer()
    {
        AssertIntConsistentWithByteBuffer(true, true);
        AssertIntConsistentWithByteBuffer(true, false);
        AssertIntConsistentWithByteBuffer(false, true);
        AssertIntConsistentWithByteBuffer(false, false);

    }

    [Fact]
    public void TestLongConsistentWithByteBuffer()
    {
        AssertLongConsistentWithByteBuffer(true, true);
        AssertLongConsistentWithByteBuffer(true, false);
        AssertLongConsistentWithByteBuffer(false, true);
        AssertLongConsistentWithByteBuffer(false, false);

    }

    // See:
    // - https://github.com/netty/netty/issues/2587
    // - https://github.com/netty/netty/issues/2580
    [Fact]
    public virtual void TestLittleEndianWithExpand()
    {
        ByteBuf buffer = NewBuffer(0);
        buffer.WriteIntLE(0x12345678);
        Assert.Equal("78563412", ByteBufUtil.HexDump(buffer));
        buffer.Release();

    }

    [Fact]
    public void ExplicitLittleEndianReadMethodsMustAlwaysUseLittleEndianByteOrder()
    {
        buffer.Clear();
        buffer.WriteBytes(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        Assert.Equal(0x0201, buffer.ReadShortLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x0201, buffer.ReadUnsignedShortLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x030201, buffer.ReadMediumLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x030201, buffer.ReadUnsignedMediumLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x04030201, buffer.ReadIntLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x04030201U, buffer.ReadUnsignedIntLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x04030201, BitConverter.SingleToInt32Bits(buffer.ReadFloatLE()));
        buffer.ReaderIndex = 0;
        Assert.Equal(0x0807060504030201L, buffer.ReadLongLE());
        buffer.ReaderIndex = 0;
        Assert.Equal(0x0807060504030201L, BitConverter.DoubleToInt64Bits(buffer.ReadDoubleLE()));
        buffer.ReaderIndex = 0;

        Assert.Equal(0x0201, buffer.GetShortLE(0));
        Assert.Equal(0x0201, buffer.GetUnsignedShortLE(0));
        Assert.Equal(0x030201, buffer.GetMediumLE(0));
        Assert.Equal(0x030201, buffer.GetUnsignedMediumLE(0));
        Assert.Equal(0x04030201, buffer.GetIntLE(0));
        Assert.Equal(0x04030201U, buffer.GetUnsignedIntLE(0));
        Assert.Equal(0x04030201, BitConverter.SingleToInt32Bits(buffer.GetFloatLE(0)));
        Assert.Equal(0x0807060504030201L, buffer.GetLongLE(0));
        Assert.Equal(0x0807060504030201L, BitConverter.DoubleToInt64Bits(buffer.GetDoubleLE(0)));

    }

    [Fact]
    public void ExplicitLittleEndianWriteMethodsMustAlwaysUseLittleEndianByteOrder()
    {
        buffer.Clear();
        buffer.WriteShortLE(0x0102);
        Assert.Equal(0x0102, buffer.ReadShortLE());
        buffer.Clear();
        buffer.WriteMediumLE(0x010203);
        Assert.Equal(0x010203, buffer.ReadMediumLE());
        buffer.Clear();
        buffer.WriteIntLE(0x01020304);
        Assert.Equal(0x01020304, buffer.ReadIntLE());
        buffer.Clear();
        buffer.WriteFloatLE(BitConverter.Int32BitsToSingle(0x01020304));
        Assert.Equal(0x01020304, BitConverter.SingleToInt32Bits(buffer.ReadFloatLE()));
        buffer.Clear();
        buffer.WriteLongLE(0x0102030405060708L);
        Assert.Equal(0x0102030405060708L, buffer.ReadLongLE());
        buffer.Clear();
        buffer.WriteDoubleLE(BitConverter.Int64BitsToDouble(0x0102030405060708L));
        Assert.Equal(0x0102030405060708L, BitConverter.DoubleToInt64Bits(buffer.ReadDoubleLE()));

        buffer.SetShortLE(0, 0x0102);
        Assert.Equal(0x0102, buffer.GetShortLE(0));
        buffer.SetMediumLE(0, 0x010203);
        Assert.Equal(0x010203, buffer.GetMediumLE(0));
        buffer.SetIntLE(0, 0x01020304);
        Assert.Equal(0x01020304, buffer.GetIntLE(0));
        buffer.SetFloatLE(0, BitConverter.Int32BitsToSingle(0x01020304));
        Assert.Equal(0x01020304, BitConverter.SingleToInt32Bits(buffer.GetFloatLE(0)));
        buffer.SetLongLE(0, 0x0102030405060708L);
        Assert.Equal(0x0102030405060708L, buffer.GetLongLE(0));
        buffer.SetDoubleLE(0, BitConverter.Int64BitsToDouble(0x0102030405060708L));
        Assert.Equal(0x0102030405060708L, BitConverter.DoubleToInt64Bits(buffer.GetDoubleLE(0)));

    }

    private void AssertShortConsistentWithByteBuffer(bool direct, bool testBigEndian)
    {
        for (int i = 0; i < JAVA_BYTEBUFFER_CONSISTENCY_ITERATIONS; ++i)
        {
            // Independent heap/native reference storage replaces the JVM ByteBuffer;
            // the BCL writes and reads the selected order without using Netty codecs.
            byte[] allocation = GC.AllocateUninitializedArray<byte>(buffer.Capacity, pinned: direct);
            Span<byte> reference = allocation;
            short expected = unchecked((short)(NextSignedInt32() & 0xFFFF));

            if (testBigEndian)
                BinaryPrimitives.WriteInt16BigEndian(reference, expected);
            else
                BinaryPrimitives.WriteInt16LittleEndian(reference, expected);

            int bufferIndex = buffer.Capacity - 2;

            if (testBigEndian)
                buffer.SetShort(bufferIndex, expected);
            else
                buffer.SetShortLE(bufferIndex, expected);

            short referenceActual = testBigEndian ? BinaryPrimitives.ReadInt16BigEndian(reference)
                : BinaryPrimitives.ReadInt16LittleEndian(reference);
            Assert.Equal(expected, referenceActual);
            Assert.Equal(referenceActual, testBigEndian ? buffer.GetShort(bufferIndex) : buffer.GetShortLE(bufferIndex));
            // A matching getter/setter endian error must not cancel itself in a roundtrip.
            Assert.Equal(reference[..2].ToArray(), ByteBufUtil.GetBytes(buffer, bufferIndex, 2));
        }
    }

    private void AssertMediumConsistentWithByteBuffer(bool direct, bool testBigEndian)
    {
        for (int i = 0; i < JAVA_BYTEBUFFER_CONSISTENCY_ITERATIONS; ++i)
        {
            // Independent heap/native reference storage replaces the JVM ByteBuffer;
            // the BCL writes and reads the selected order without using Netty codecs.
            byte[] allocation = GC.AllocateUninitializedArray<byte>(buffer.Capacity, pinned: direct);
            Span<byte> reference = allocation;
            int expected = NextSignedInt32() & 0x00FFFFFF;

            if (testBigEndian)
                BinaryPrimitives.WriteInt32BigEndian(reference, expected);
            else
                BinaryPrimitives.WriteInt32LittleEndian(reference, expected);

            int bufferIndex = buffer.Capacity - 3;

            if (testBigEndian)
                buffer.SetMedium(bufferIndex, expected);
            else
                buffer.SetMediumLE(bufferIndex, expected);

            int referenceActual = testBigEndian ? BinaryPrimitives.ReadInt32BigEndian(reference)
                : BinaryPrimitives.ReadInt32LittleEndian(reference);
            Assert.Equal(expected, referenceActual);
            Assert.Equal(referenceActual, testBigEndian ? buffer.GetUnsignedMedium(bufferIndex) : buffer.GetUnsignedMediumLE(bufferIndex));
            // A matching getter/setter endian error must not cancel itself in a roundtrip.
            Assert.Equal((testBigEndian ? reference.Slice(1, 3) : reference[..3]).ToArray(), ByteBufUtil.GetBytes(buffer, bufferIndex, 3));
        }
    }

    private void AssertIntConsistentWithByteBuffer(bool direct, bool testBigEndian)
    {
        for (int i = 0; i < JAVA_BYTEBUFFER_CONSISTENCY_ITERATIONS; ++i)
        {
            // Independent heap/native reference storage replaces the JVM ByteBuffer;
            // the BCL writes and reads the selected order without using Netty codecs.
            byte[] allocation = GC.AllocateUninitializedArray<byte>(buffer.Capacity, pinned: direct);
            Span<byte> reference = allocation;
            int expected = NextSignedInt32();

            if (testBigEndian)
                BinaryPrimitives.WriteInt32BigEndian(reference, expected);
            else
                BinaryPrimitives.WriteInt32LittleEndian(reference, expected);

            int bufferIndex = buffer.Capacity - 4;

            if (testBigEndian)
                buffer.SetInt(bufferIndex, expected);
            else
                buffer.SetIntLE(bufferIndex, expected);

            int referenceActual = testBigEndian ? BinaryPrimitives.ReadInt32BigEndian(reference)
                : BinaryPrimitives.ReadInt32LittleEndian(reference);
            Assert.Equal(expected, referenceActual);
            Assert.Equal(referenceActual, testBigEndian ? buffer.GetInt(bufferIndex) : buffer.GetIntLE(bufferIndex));
            // A matching getter/setter endian error must not cancel itself in a roundtrip.
            Assert.Equal(reference[..4].ToArray(), ByteBufUtil.GetBytes(buffer, bufferIndex, 4));
        }
    }

    private void AssertLongConsistentWithByteBuffer(bool direct, bool testBigEndian)
    {
        for (int i = 0; i < JAVA_BYTEBUFFER_CONSISTENCY_ITERATIONS; ++i)
        {
            // Independent heap/native reference storage replaces the JVM ByteBuffer;
            // the BCL writes and reads the selected order without using Netty codecs.
            byte[] allocation = GC.AllocateUninitializedArray<byte>(buffer.Capacity, pinned: direct);
            Span<byte> reference = allocation;
            long expected = NextSignedInt64();

            if (testBigEndian)
                BinaryPrimitives.WriteInt64BigEndian(reference, expected);
            else
                BinaryPrimitives.WriteInt64LittleEndian(reference, expected);

            int bufferIndex = buffer.Capacity - 8;

            if (testBigEndian)
                buffer.SetLong(bufferIndex, expected);
            else
                buffer.SetLongLE(bufferIndex, expected);

            long referenceActual = testBigEndian ? BinaryPrimitives.ReadInt64BigEndian(reference)
                : BinaryPrimitives.ReadInt64LittleEndian(reference);
            Assert.Equal(expected, referenceActual);
            Assert.Equal(referenceActual, testBigEndian ? buffer.GetLong(bufferIndex) : buffer.GetLongLE(bufferIndex));
            // A matching getter/setter endian error must not cancel itself in a roundtrip.
            Assert.Equal(reference[..8].ToArray(), ByteBufUtil.GetBytes(buffer, bufferIndex, 8));
        }
    }

    private int NextSignedInt32()
    {
        Span<byte> bits = stackalloc byte[sizeof(int)];
        random.NextBytes(bits);

        return BinaryPrimitives.ReadInt32BigEndian(bits);
    }

    private long NextSignedInt64()
    {
        Span<byte> bits = stackalloc byte[sizeof(long)];
        random.NextBytes(bits);

        return BinaryPrimitives.ReadInt64BigEndian(bits);
    }

    // CLR: the inherited derived-view contents, capacity and release-original bodies of the original
    // AbstractByteBufTest, run against every concrete fixture's NewBuffer as in the source.
    [Fact]
    public void TestRetainedSliceContents()
    {
        TestSliceContents0(true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal1()
    {
        TestSliceReleaseOriginal(true, true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal2()
    {
        TestSliceReleaseOriginal(true, false);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal3()
    {
        TestSliceReleaseOriginal(false, true);
    }

    [Fact]
    public void TestRetainedSliceReleaseOriginal4()
    {
        TestSliceReleaseOriginal(false, false);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal1()
    {
        TestDuplicateReleaseOriginal(true, true);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal2()
    {
        TestDuplicateReleaseOriginal(true, false);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal3()
    {
        TestDuplicateReleaseOriginal(false, true);
    }

    [Fact]
    public void TestRetainedDuplicateReleaseOriginal4()
    {
        TestDuplicateReleaseOriginal(false, false);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal1()
    {
        TestMultipleRetainedSliceReleaseOriginal(true, true);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal2()
    {
        TestMultipleRetainedSliceReleaseOriginal(true, false);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal3()
    {
        TestMultipleRetainedSliceReleaseOriginal(false, true);
    }

    [Fact]
    public void TestMultipleRetainedSliceReleaseOriginal4()
    {
        TestMultipleRetainedSliceReleaseOriginal(false, false);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal1()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(true, true);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal2()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(true, false);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal3()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(false, true);
    }

    [Fact]
    public void TestMultipleRetainedDuplicateReleaseOriginal4()
    {
        TestMultipleRetainedDuplicateReleaseOriginal(false, false);
    }

    [Fact]
    public void TestSliceContents()
    {
        TestSliceContents0(false);
    }

    [Fact]
    public void TestRetainedDuplicateContents()
    {
        TestDuplicateContents0(true);
    }

    [Fact]
    public void TestDuplicateContents()
    {
        TestDuplicateContents0(false);
    }

    [Fact]
    public virtual void TestDuplicateCapacityChange()
    {
        TestDuplicateCapacityChange0(false);
    }

    [Fact]
    public virtual void TestRetainedDuplicateCapacityChange()
    {
        TestDuplicateCapacityChange0(true);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateCapacityChange test name.
    private void TestDuplicateCapacityChange0(bool retainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();

        try
        {
            dup.Capacity = 10;
            Assert.Equal(buf.Capacity, dup.Capacity);
            dup.Capacity = 5;
            Assert.Equal(buf.Capacity, dup.Capacity);
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }

            buf.Release();
        }
    }

    // CLR: xUnit1024 forbids overloading the TestSliceContents test name.
    private void TestSliceContents0(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(3).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(new byte[] { 4, 5, 6 });
        ByteBuf slice = retainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 3, 3)
                                      : buf.Slice(buf.ReaderIndex + 3, 3);

        try
        {
            Assert.Equal(0, slice.CompareTo(expected));
            Assert.Equal(0, slice.CompareTo(slice.Duplicate()));
            ByteBuf b = slice.RetainedDuplicate();
            Assert.Equal(0, slice.CompareTo(b));
            b.Release();
            Assert.Equal(0, slice.CompareTo(slice.Slice(0, slice.Capacity)));
        }
        finally
        {
            if (retainedSlice)
            {
                slice.Release();
            }

            buf.Release();
            expected.Release();
        }
    }

    private void TestSliceReleaseOriginal(bool retainedSlice1, bool retainedSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(3).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(2).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 6, 7, 8 });
        expected2.WriteBytes(new byte[] { 7, 8 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(0, slice2.CompareTo(expected2));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
    }

    private void TestDuplicateReleaseOriginal(bool retainedDuplicate1, bool retainedDuplicate2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(buf, buf.ReaderIndex, buf.ReadableBytes);
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(0, dup1.CompareTo(expected));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(0, dup2.CompareTo(expected));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected.Release());

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    private void TestMultipleRetainedSliceReleaseOriginal(bool retainedSlice1, bool retainedSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(3).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(2).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(2).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 6, 7, 8 });
        expected2.WriteBytes(new byte[] { 7, 8 });
        expected3.WriteBytes(new byte[] { 6, 7 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(0, slice2.CompareTo(expected2));

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        ByteBuf slice3 = slice1.RetainedSlice(slice1.ReaderIndex, 2);
        Assert.Equal(0, slice3.CompareTo(expected3));

        // The handler created another slice of the slice and is now done with it.
        slice3.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, slice3.RefCnt);
    }

    private void TestMultipleRetainedDuplicateReleaseOriginal(bool retainedDuplicate1, bool retainedDuplicate2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected.WriteBytes(buf, buf.ReaderIndex, buf.ReadableBytes);
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(0, dup1.CompareTo(expected));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(0, dup2.CompareTo(expected));
        Assert.Equal(0, dup2.CompareTo(dup2.Duplicate()));
        Assert.Equal(0, dup2.CompareTo(dup2.Slice()));

        ByteBuf tmpBuf = dup2.RetainedDuplicate();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();
        tmpBuf = dup2.RetainedSlice();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        ByteBuf dup3 = dup1.RetainedDuplicate();
        Assert.Equal(0, dup3.CompareTo(expected));

        // The handler created another slice of the slice and is now done with it.
        dup3.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Cleanup the expected buffers used for testing.
        Assert.True(expected.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
        Assert.Equal(0, dup3.RefCnt);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateContents test name.
    private void TestDuplicateContents0(bool retainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();

        try
        {
            Assert.Equal(0, dup.CompareTo(buf));
            Assert.Equal(0, dup.CompareTo(dup.Duplicate()));
            ByteBuf b = dup.RetainedDuplicate();
            Assert.Equal(0, dup.CompareTo(b));
            b.Release();
            Assert.Equal(0, dup.CompareTo(dup.Slice(dup.ReaderIndex, dup.ReadableBytes)));
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }

            buf.Release();
        }
    }

    [Fact]
    public void TestDuplicateRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.RefCnt);
        Assert.True(buf.Duplicate().Release());
        Assert.Equal(0, buf.RefCnt);
    }

    // CLR: the two inherited bodies that only exercise the deprecated ByteBuf.order()/order(ByteOrder) facade
    // ("use the Little Endian accessors, e.g. getShortLE, getIntLE instead of creating a buffer with swapped
    // endianness"). They keep the existing explicit-accessor CLR adaptation (fixed BE/LE accessors instead of
    // mutable order views) and now run on every concrete fixture's buffers; the five-provider versions remain as
    // supplements in ByteBufComparisonSourceTest and ByteBufSnapshotSourceTest.
    [Fact]
    public void TestSliceEndianness()
    {
        // CLR: CLR ByteBuf has no mutable order() facade. Known bytes let explicit BE/LE accessors show that
        // each slice reads the parent's wire bytes in both byte orders.
        byte[] bytes = new byte[buffer.Capacity];
        random.NextBytes(bytes);

        for (int i = 0; i < bytes.Length; ++i)
        {
            buffer.SetByte(i, bytes[i]);
        }

        AssertSliceEndianness(bytes, buffer.Slice(0, buffer.Capacity), 0, buffer.Capacity);
        AssertSliceEndianness(bytes, buffer.Slice(0, buffer.Capacity - 1), 0, buffer.Capacity - 1);
        AssertSliceEndianness(bytes, buffer.Slice(1, buffer.Capacity - 1), 1, buffer.Capacity - 1);
        AssertSliceEndianness(bytes, buffer.Slice(1, buffer.Capacity - 2), 1, buffer.Capacity - 2);
    }

    private static void AssertSliceEndianness(byte[] bytes, ByteBuf slice, int offset, int length)
    {
        // CLR: explicit BE/LE accessors replace the mutable Java order identities.
        Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)), slice.GetInt(0));
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)), slice.GetIntLE(0));
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(length, slice.WriterIndex);
    }

    [Fact]
    public void TestCompareTo2()
    {
        byte[] bytes = { 1, 2, 3, 4 };
        byte[] bytesReversed = { 4, 3, 2, 1 };

        // CLR: there is no order(LITTLE_ENDIAN/BIG_ENDIAN) facade; buf1/buf2 are read through explicit LE
        // accessors and buf3/buf4 through BE accessors, while comparison uses the fixed byte content.
        ByteBuf buf1 = NewBuffer(4).Clear().WriteBytes(bytes);
        ByteBuf buf2 = NewBuffer(4).Clear().WriteBytes(bytesReversed);
        ByteBuf buf3 = NewBuffer(4).Clear().WriteBytes(bytes);
        ByteBuf buf4 = NewBuffer(4).Clear().WriteBytes(bytesReversed);

        try
        {
            Assert.Equal(0x04030201, buf1.GetIntLE(0));
            Assert.Equal(0x01020304, buf2.GetIntLE(0));
            Assert.Equal(0x01020304, buf3.GetInt(0));
            Assert.Equal(0x04030201, buf4.GetInt(0));
            Assert.Equal(buf1.CompareTo(buf2), buf3.CompareTo(buf4));
            Assert.Equal(buf2.CompareTo(buf1), buf4.CompareTo(buf3));
            Assert.Equal(buf1.CompareTo(buf3), buf2.CompareTo(buf4));
            Assert.Equal(buf3.CompareTo(buf1), buf4.CompareTo(buf2));
        }
        finally
        {
            buf1.Release();
            buf2.Release();
            buf3.Release();
            buf4.Release();
        }
    }

    protected virtual bool DiscardReadBytesDoesNotMoveWritableBytes()
    {
        return true;
    }

    [Fact]
    public void ReaderIndexBoundaryCheck1()
    {
        try
        {
            buffer.WriterIndex = 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = -1;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck2()
    {
        try
        {
            buffer.WriterIndex = buffer.Capacity;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = buffer.Capacity + 1;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck3()
    {
        try
        {
            buffer.WriterIndex = CAPACITY / 2;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.ReaderIndex = CAPACITY * 3 / 2;
        });
    }

    [Fact]
    public void ReaderIndexBoundaryCheck4()
    {
        buffer.WriterIndex = 0;
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = buffer.Capacity;
        buffer.ReaderIndex = buffer.Capacity;
    }

    [Fact]
    public void WriterIndexBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = -1;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck2()
    {
        try
        {
            buffer.WriterIndex = CAPACITY;
            buffer.ReaderIndex = CAPACITY;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = buffer.Capacity + 1;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck3()
    {
        try
        {
            buffer.WriterIndex = CAPACITY;
            buffer.ReaderIndex = CAPACITY / 2;
        }
        catch (ArgumentOutOfRangeException)
        {
            Assert.Fail();
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.WriterIndex = CAPACITY / 4;
        });
    }

    [Fact]
    public void WriterIndexBoundaryCheck4()
    {
        buffer.WriterIndex = 0;
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = CAPACITY;

        buffer.WriteBytes(ReadOnlySpan<byte>.Empty);
    }

    [Fact]
    public void SetIndexBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(-1, CAPACITY);
        });
    }

    [Fact]
    public void SetIndexBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(CAPACITY / 2, CAPACITY / 4);
        });
    }

    [Fact]
    public void SetIndexBoundaryCheck3()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.SetIndex(0, CAPACITY + 1);
        });
    }

    [Fact]
    public void TestSkipBytes1()
    {
        buffer.SetIndex(CAPACITY / 4, CAPACITY / 2);

        buffer.SkipBytes(CAPACITY / 4);
        Assert.Equal(CAPACITY / 4 * 2, buffer.ReaderIndex);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SkipBytes(CAPACITY / 4 + 1));

        // Should remain unchanged.
        Assert.Equal(CAPACITY / 4 * 2, buffer.ReaderIndex);
    }

    [Fact]
    public void TestDiscardReadBytes()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            buffer.WriteInt(i);
        }

        ByteBuf copy = Own(Unpooled.CopiedBuffer(buffer));

        // Make sure there's no effect if called when readerIndex is 0.
        buffer.ReaderIndex = CAPACITY / 4;
        buffer.MarkReaderIndex();
        buffer.WriterIndex = CAPACITY / 3;
        buffer.MarkWriterIndex();
        buffer.ReaderIndex = 0;
        buffer.WriterIndex = CAPACITY / 2;
        buffer.DiscardReadBytes();

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2, buffer.WriterIndex);
        Assert.Equal(copy.Slice(0, CAPACITY / 2), buffer.Slice(0, CAPACITY / 2));
        buffer.ResetReaderIndex();
        Assert.Equal(CAPACITY / 4, buffer.ReaderIndex);
        buffer.ResetWriterIndex();
        Assert.Equal(CAPACITY / 3, buffer.WriterIndex);

        // Make sure bytes after writerIndex is not copied.
        buffer.ReaderIndex = 1;
        buffer.WriterIndex = CAPACITY / 2;
        buffer.DiscardReadBytes();

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2 - 1, buffer.WriterIndex);
        Assert.Equal(copy.Slice(1, CAPACITY / 2 - 1), buffer.Slice(0, CAPACITY / 2 - 1));

        if (DiscardReadBytesDoesNotMoveWritableBytes())
        {
            // If writable bytes were copied, the test should fail to avoid unnecessary memory bandwidth consumption.
            Assert.False(copy.Slice(CAPACITY / 2, CAPACITY / 2).Equals(buffer.Slice(CAPACITY / 2 - 1, CAPACITY / 2)));
        }
        else
        {
            Assert.Equal(copy.Slice(CAPACITY / 2, CAPACITY / 2), buffer.Slice(CAPACITY / 2 - 1, CAPACITY / 2));
        }

        // Marks also should be relocated.
        buffer.ResetReaderIndex();
        Assert.Equal(CAPACITY / 4 - 1, buffer.ReaderIndex);
        buffer.ResetWriterIndex();
        Assert.Equal(CAPACITY / 3 - 1, buffer.WriterIndex);
        copy.Release();
    }

    /**
     * The similar test case with {@link #testDiscardReadBytes()} but this one
     * discards a large chunk at once.
     */
    [Fact]
    public void TestDiscardReadBytes2()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            buffer.WriteByte(unchecked((byte)i));
        }

        ByteBuf copy = Own(Unpooled.CopiedBuffer(buffer));

        // Discard the first (CAPACITY / 2 - 1) bytes.
        buffer.SetIndex(CAPACITY / 2 - 1, CAPACITY - 1);
        buffer.DiscardReadBytes();
        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(CAPACITY / 2, buffer.WriterIndex);

        for (int i = 0; i < CAPACITY / 2; i ++)
        {
            Assert.Equal(copy.Slice(CAPACITY / 2 - 1 + i, CAPACITY / 2 - i), buffer.Slice(i, CAPACITY / 2 - i));
        }

        copy.Release();
    }

    // Test case for https://github.com/netty/netty/issues/325
    [Fact]
    public void TestDiscardAllReadBytes()
    {
        buffer.WriterIndex = buffer.Capacity;
        buffer.ReaderIndex = buffer.WriterIndex;
        buffer.DiscardReadBytes();
    }

    [Fact]
    public void TestDiscardReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().DiscardReadBytes();
        });
    }

    [Fact]
    public void TestDiscardSomeReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().DiscardSomeReadBytes();
        });
    }

    private ByteBuf ReleasedBuffer()
    {
        ByteBuf buffer = NewBuffer(8);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        buffer.Clear();
        Assert.True(buffer.Release());
        return buffer;
    }

    // CLR: the inherited released-buffer, derived-view lifetime, unreleasable-wrapper and refCnt bodies of
    // the original AbstractByteBufTest, run against every concrete fixture's NewBuffer as in the source.
    // ByteBufLifetimeFixtureTest keeps the same bodies over its eight-provider/three-entry-form matrix.
    [Fact]
    public void TestGetBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().GetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, stream, 1);
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, socket, 1);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().SetBytes(0, buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, stream, 1);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, socket, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease4()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().ReadBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadBytesAfterRelease7()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease8()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(stream, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease9()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(stream, 1);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease10()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(socket, 1);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease2()
    {
        ByteBuf buffer = Unpooled.CopiedBuffer(new byte[8]);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease3()
    {
        ByteBuf buffer = Unpooled.Buffer(8);

        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                ReleasedBuffer().WriteBytes(buffer, 0, 1);
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestWriteBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8].AsSpan());
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease7()
    {
        using var stream = new MemoryStream(new byte[8]);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(stream, 1);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease8()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(socket, 1);
        });
    }

    [Fact]
    public void TestNioBufferAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlyMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestNioBufferAfterRelease1()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsReadOnlyMemory(0, 1));
    }

    [Fact]
    public virtual void TestInternalNioBufferAfterRelease()
    {
        TestInternalNioBufferAfterRelease0<IllegalReferenceCountException>();
    }

    // CLR: the expected exception type is a generic argument instead of a Class<? extends Throwable>.
    protected void TestInternalNioBufferAfterRelease0<T>() where T : Exception
    {
        ByteBuf releasedBuffer = ReleasedBuffer();
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<T>(() => releasedBuffer.AsMemory(releasedBuffer.ReaderIndex, 1));
    }

    [Fact]
    public void TestNioBuffersAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        ByteBuf buffer = ReleasedBuffer();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlySequence(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestNioBuffersAfterRelease2()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsReadOnlySequence(0, 1));
    }

    [Fact]
    public void TestArrayAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() => ReleasedBuffer().AsMemory(0, 0));
    }

    [Fact]
    public void TestMemoryAddressAfterRelease()
    {
        // CLR: bounded borrows replace JVM NIO, array and permanent-address exposure.
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            using var pin = ReleasedBuffer().AsReadOnlyMemory(0, 0).Pin();
        });
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedSliceRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestSliceAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedSliceRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedSliceFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertRetainedSliceFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedSliceDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        ByteBuf buf3 = buf2.Duplicate();
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestDuplicateAfterReleaseRetainedDuplicateSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        ByteBuf buf3 = buf2.Slice(0, 1);
        AssertDuplicateFailAfterRelease(buf, buf2, buf3);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseRetainedDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedDuplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseDuplicate()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.Duplicate();
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedDuplicateAfterReleaseRetainedSlice()
    {
        ByteBuf buf = NewBuffer(1);
        ByteBuf buf2 = buf.RetainedSlice(0, 1);
        AssertRetainedDuplicateFailAfterRelease(buf, buf2);
    }

    [Fact]
    public void TestRetainedSliceAndRetainedDuplicateContentIsExpected()
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(5).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(4).ResetWriterIndex();
        ByteBuf expected4 = NewBuffer(3).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6, 7 });
        expected3.WriteBytes(new byte[] { 4, 5, 6, 7 });
        expected4.WriteBytes(new byte[] { 5, 6, 7 });

        ByteBuf slice1 = buf.RetainedSlice(buf.ReaderIndex + 1, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        Assert.Equal(0, slice1.CompareTo(buf.Slice(buf.ReaderIndex + 1, 6)));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        // Advance the reader index on the slice.
        slice1.ReadByte();

        ByteBuf dup1 = slice1.RetainedDuplicate();
        Assert.Equal(0, dup1.CompareTo(expected2));
        Assert.Equal(0, dup1.CompareTo(slice1.Duplicate()));

        // Advance the reader index on dup1.
        dup1.ReadByte();

        ByteBuf dup2 = dup1.Duplicate();
        Assert.Equal(0, dup2.CompareTo(expected3));

        // Advance the reader index on dup2.
        dup2.ReadByte();

        ByteBuf slice2 = dup2.RetainedSlice(dup2.ReaderIndex, 3);
        Assert.Equal(0, slice2.CompareTo(expected4));
        Assert.Equal(0, slice2.CompareTo(dup2.Slice(dup2.ReaderIndex, 3)));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());
        Assert.True(expected4.Release());

        slice2.Release();
        dup2.Release();

        Assert.Equal(slice2.RefCnt, dup2.RefCnt);
        Assert.Equal(dup2.RefCnt, dup1.RefCnt);

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    [Fact]
    public void TestRetainedDuplicateAndRetainedSliceContentIsExpected()
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(5).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(4).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6, 7 });
        expected3.WriteBytes(new byte[] { 5, 6, 7 });

        ByteBuf dup1 = buf.RetainedDuplicate();
        Assert.Equal(0, dup1.CompareTo(buf));
        Assert.Equal(0, dup1.CompareTo(buf.Slice()));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        // Advance the reader index on the dup.
        dup1.ReadByte();

        ByteBuf slice1 = dup1.RetainedSlice(dup1.ReaderIndex, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        Assert.Equal(0, slice1.CompareTo(slice1.Duplicate()));

        // Advance the reader index on slice1.
        slice1.ReadByte();

        ByteBuf dup2 = slice1.Duplicate();
        Assert.Equal(0, dup2.CompareTo(slice1));

        // Advance the reader index on dup2.
        dup2.ReadByte();

        ByteBuf slice2 = dup2.RetainedSlice(dup2.ReaderIndex + 1, 3);
        Assert.Equal(0, slice2.CompareTo(expected3));
        Assert.Equal(0, slice2.CompareTo(dup2.Slice(dup2.ReaderIndex + 1, 3)));

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());

        slice2.Release();
        slice1.Release();

        Assert.Equal(slice2.RefCnt, dup2.RefCnt);
        Assert.Equal(dup2.RefCnt, slice1.RefCnt);

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice1()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(true, true);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice2()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(true, false);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice3()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(false, true);
    }

    [Fact]
    public void TestMultipleLevelRetainedSlice4()
    {
        TestMultipleLevelRetainedSliceWithNonRetained(false, false);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable1()
    {
        TestRetainedSliceUnreleasable(true, true);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable2()
    {
        TestRetainedSliceUnreleasable(true, false);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable3()
    {
        TestRetainedSliceUnreleasable(false, true);
    }

    [Fact]
    public void TestRetainedSliceUnreleasable4()
    {
        TestRetainedSliceUnreleasable(false, false);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable1()
    {
        TestReadRetainedSliceUnreleasable(true, true);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable2()
    {
        TestReadRetainedSliceUnreleasable(true, false);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable3()
    {
        TestReadRetainedSliceUnreleasable(false, true);
    }

    [Fact]
    public void TestReadRetainedSliceUnreleasable4()
    {
        TestReadRetainedSliceUnreleasable(false, false);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable1()
    {
        TestRetainedDuplicateUnreleasable(true, true);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable2()
    {
        TestRetainedDuplicateUnreleasable(true, false);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable3()
    {
        TestRetainedDuplicateUnreleasable(false, true);
    }

    [Fact]
    public void TestRetainedDuplicateUnreleasable4()
    {
        TestRetainedDuplicateUnreleasable(false, false);
    }

    // Test-case trying to reproduce:
    // https://github.com/netty/netty/issues/2843
    [Fact]
    public void TestRefCnt()
    {
        TestRefCnt0(false);
    }

    [Fact]
    public void TestRefCnt2()
    {
        TestRefCnt0(true);
    }

    private static void AssertSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }

        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.Slice();
            });
        }
    }

    private static void AssertRetainedSliceFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }

        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.RetainedSlice();
            });
        }
    }

    private static void AssertDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }

        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.Duplicate();
            });
        }
    }

    private static void AssertRetainedDuplicateFailAfterRelease(params ByteBuf[] bufs)
    {
        foreach (ByteBuf buf in bufs)
        {
            if (buf.RefCnt > 0)
            {
                buf.Release();
            }
        }

        foreach (ByteBuf buf in bufs)
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                Assert.Equal(0, buf.RefCnt);
                buf.RetainedDuplicate();
            });
        }
    }

    private void TestRetainedSliceUnreleasable(bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.RetainedSlice() : buf2.Slice().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestReadRetainedSliceUnreleasable(bool initRetainedSlice, bool finalRetainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedSlice ? buf.RetainedSlice() : buf.Slice().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedSlice ? buf2.ReadRetainedSlice(buf2.ReadableBytes)
                                          : buf2.ReadSlice(buf2.ReadableBytes).Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestRetainedDuplicateUnreleasable(bool initRetainedDuplicate, bool finalRetainedDuplicate)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf buf1 = initRetainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate().Retain();
        ByteBuf buf2 = Unpooled.UnreleasableBuffer(buf1);
        ByteBuf buf3 = finalRetainedDuplicate ? buf2.RetainedDuplicate() : buf2.Duplicate().Retain();
        Assert.False(buf3.Release());
        Assert.False(buf2.Release());
        buf1.Release();
        Assert.True(buf.Release());
        Assert.Equal(0, buf1.RefCnt);
        Assert.Equal(0, buf.RefCnt);
    }

    private void TestMultipleLevelRetainedSliceWithNonRetained(bool doSlice1, bool doSlice2)
    {
        ByteBuf buf = NewBuffer(8).ResetWriterIndex();
        ByteBuf expected1 = NewBuffer(6).ResetWriterIndex();
        ByteBuf expected2 = NewBuffer(4).ResetWriterIndex();
        ByteBuf expected3 = NewBuffer(2).ResetWriterIndex();
        ByteBuf expected4SliceSlice = NewBuffer(1).ResetWriterIndex();
        ByteBuf expected4DupSlice = NewBuffer(1).ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        expected1.WriteBytes(new byte[] { 2, 3, 4, 5, 6, 7 });
        expected2.WriteBytes(new byte[] { 3, 4, 5, 6 });
        expected3.WriteBytes(new byte[] { 4, 5 });
        expected4SliceSlice.WriteBytes(new byte[] { 5 });
        expected4DupSlice.WriteBytes(new byte[] { 4 });

        ByteBuf slice1 = buf.RetainedSlice(buf.ReaderIndex + 1, 6);
        Assert.Equal(0, slice1.CompareTo(expected1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = slice1.RetainedSlice(slice1.ReaderIndex + 1, 4);
        Assert.Equal(0, slice2.CompareTo(expected2));
        Assert.Equal(0, slice2.CompareTo(slice2.Duplicate()));
        Assert.Equal(0, slice2.CompareTo(slice2.Slice()));

        ByteBuf tmpBuf = slice2.RetainedDuplicate();
        Assert.Equal(0, slice2.CompareTo(tmpBuf));
        tmpBuf.Release();
        tmpBuf = slice2.RetainedSlice();
        Assert.Equal(0, slice2.CompareTo(tmpBuf));
        tmpBuf.Release();

        ByteBuf slice3 = doSlice1 ? slice2.Slice(slice2.ReaderIndex + 1, 2) : slice2.Duplicate();

        if (doSlice1)
        {
            Assert.Equal(0, slice3.CompareTo(expected3));
        }
        else
        {
            Assert.Equal(0, slice3.CompareTo(expected2));
        }

        ByteBuf slice4 = doSlice2 ? slice3.Slice(slice3.ReaderIndex + 1, 1) : slice3.Duplicate();

        if (doSlice1 && doSlice2)
        {
            Assert.Equal(0, slice4.CompareTo(expected4SliceSlice));
        }
        else if (doSlice2)
        {
            Assert.Equal(0, slice4.CompareTo(expected4DupSlice));
        }
        else
        {
            Assert.Equal(0, slice3.CompareTo(slice4));
        }

        // Cleanup the expected buffers used for testing.
        Assert.True(expected1.Release());
        Assert.True(expected2.Release());
        Assert.True(expected3.Release());
        Assert.True(expected4SliceSlice.Release());
        Assert.True(expected4DupSlice.Release());

        // Slice 4, 3, and 2 should effectively "share" a reference count.
        slice4.Release();
        Assert.Equal(slice3.RefCnt, slice2.RefCnt);
        Assert.Equal(slice3.RefCnt, slice4.RefCnt);

        // Slice 1 should also release the original underlying buffer without throwing exceptions
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, slice3.RefCnt);
    }

    private void TestRefCnt0(bool parameter)
    {
        for (int i = 0; i < 10; ++i)
        {
            using var latch = new ManualResetEventSlim();
            using var innerLatch = new ManualResetEventSlim();
            ByteBuf buffer = NewBuffer(4);
            Assert.Equal(1, buffer.RefCnt);
            Exception innerThrowable = null;
            int count = int.MaxValue;
            Thread reader = null;
            var releaser = new Thread(() =>
            {
                try
                {
                    Assert.True(parameter ? buffer.Release(buffer.RefCnt) : buffer.Release());
                    reader = new Thread(() =>
                    {
                        count = buffer.RefCnt;
                        latch.Set();
                    });
                    reader.Start();
                    // Keep Thread alive a bit so the ThreadLocal caches are not freed
                    innerLatch.Wait();
                }
                catch (Exception cause)
                {
                    innerThrowable = cause;
                    latch.Set();
                }
            });
            releaser.Start();

            try
            {
                Assert.True(latch.Wait(TimeSpan.FromSeconds(30)));
                Assert.Null(innerThrowable);
                Assert.Equal(0, count);
            }
            finally
            {
                innerLatch.Set();
                releaser.Join();
                reader?.Join();

                if (buffer.RefCnt > 0)
                    buffer.Release();
            }
        }
    }

    [Fact]
    public void GetByteBufferBoundaryCheck()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, new byte[0]));
    }

    [Fact]
    public void GetByteBufferState()
    {
        byte[] dst = new byte[4];
        Memory<byte> window = dst.AsMemory(1, 2);
        buffer.SetByte(0, 1);
        buffer.SetByte(1, 2);
        buffer.SetByte(2, 3);
        buffer.SetByte(3, 4);
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;
        buffer.GetBytes(1, window.Span);

        // CLR: immutable range bounds replace ByteBuffer position/limit; no synthetic cursor is advanced.
        Assert.Equal((byte)0, dst[0]);
        Assert.Equal((byte)2, dst[1]);
        Assert.Equal((byte)3, dst[2]);
        Assert.Equal((byte)0, dst[3]);
        Assert.Equal(readerIndex, buffer.ReaderIndex);
        Assert.Equal(writerIndex, buffer.WriterIndex);
    }

    [Fact]
    public void GetDirectByteBufferBoundaryCheck()
    {
        byte[] destination = GC.AllocateArray<byte>(1, pinned: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, destination.AsSpan(0, 0)));
    }

    [Fact]
    public void GetDirectByteBufferState()
    {
        Span<byte> dst = GC.AllocateArray<byte>(4, pinned: true);
        Span<byte> window = dst.Slice(1, 2);
        buffer.SetByte(0, 1);
        buffer.SetByte(1, 2);
        buffer.SetByte(2, 3);
        buffer.SetByte(3, 4);
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;
        buffer.GetBytes(1, window);

        // JVM direct allocation is zero-initialized; The zeroed array preserves the two original guard bytes.
        Assert.Equal((byte)0, dst[0]);
        Assert.Equal((byte)2, dst[1]);
        Assert.Equal((byte)3, dst[2]);
        Assert.Equal((byte)0, dst[3]);
        Assert.Equal(readerIndex, buffer.ReaderIndex);
        Assert.Equal(writerIndex, buffer.WriterIndex);
    }

    [Fact]
    public void TestRandomByteBufferTransfer()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.SetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Memory<byte> window = value.AsMemory(valueOffset, BLOCK_SIZE);
            byte[] before = (byte[])value.Clone();
            buffer.GetBytes(i, window.Span);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
                Assert.Equal(expectedValue[j], value[j]);

            // Preserve the selected external range, including bytes before and after it.
            Assert.Equal(before.AsSpan(0, valueOffset).ToArray(), value.AsSpan(0, valueOffset).ToArray());
            Assert.Equal(before.AsSpan(valueOffset + BLOCK_SIZE).ToArray(), value.AsSpan(valueOffset + BLOCK_SIZE).ToArray());
            Assert.Equal(readerIndex, buffer.ReaderIndex);
            Assert.Equal(writerIndex, buffer.WriterIndex);
        }
    }

    [Fact]
    public void TestSequentialByteBufferTransfer()
    {
        buffer.WriterIndex = 0;
        byte[] value = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.WriteBytes(value.AsSpan(valueOffset, BLOCK_SIZE));
            Assert.Equal(i + BLOCK_SIZE, buffer.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Memory<byte> window = value.AsMemory(valueOffset, BLOCK_SIZE);
            byte[] before = (byte[])value.Clone();
            buffer.ReadBytes(window.Span);

            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
                Assert.Equal(expectedValue[j], value[j]);

            // Span has no advancing cursor; the real ByteBuf must consume the entire selected range.
            Assert.Equal(i + BLOCK_SIZE, buffer.ReaderIndex);
            Assert.Equal(before.AsSpan(0, valueOffset).ToArray(), value.AsSpan(0, valueOffset).ToArray());
            Assert.Equal(before.AsSpan(valueOffset + BLOCK_SIZE).ToArray(), value.AsSpan(valueOffset + BLOCK_SIZE).ToArray());
        }
    }

    [Fact]
    public virtual void TestNioBufferExposeOnlyRegion()
    {
        ByteBuf buffer = NewBuffer(8);

        try
        {
            byte[] data = new byte[8];
            random.NextBytes(data);
            buffer.WriteBytes(data);
            var reader = new SequenceReader<byte>(buffer.AsReadOnlySequence(1, data.Length - 2));
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(6, reader.Remaining);

            for (int i = 1; reader.TryRead(out byte actual); i++)
                Assert.Equal(data[i], actual);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestEmptyNioBuffers()
    {
        ByteBuf buffer = NewBuffer(8);

        try
        {
            buffer.Clear();
            Assert.False(buffer.IsReadable());
            ReadOnlySequence<byte> sequence = buffer.ReadableSequence;
            // CLR: an empty sequence is one logical empty range, without a Java array or cursor object.
            Assert.True(sequence.IsSingleSegment);
            Assert.True(sequence.First.IsEmpty);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public virtual void TestReadBytes()
    {
        ByteBuf buffer = NewBuffer(8);
        ByteBuf buffer2 = null;

        try
        {
            byte[] bytes = new byte[8];
            buffer.WriteBytes(bytes);
            buffer2 = buffer.ReadBytes(4);
            Assert.Same(buffer.Alloc(), buffer2.Alloc());
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.RefCnt);
            Assert.True(buffer2.Release());
            Assert.Equal(0, buffer2.RefCnt);
        }
        finally
        {
            if (buffer.RefCnt > 0)
            {
                buffer.Release();
            }

            if (buffer2?.RefCnt > 0)
            {
                buffer2.Release();
            }
        }
    }

    [Fact]
    public virtual void TestGetBytesByteBuffer()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is in the ByteBuf.
        byte[] nioBuffer = new byte[bytes.Length + 1];
        ByteBuf buffer = NewBuffer(bytes.Length);

        try
        {
            buffer.WriteBytes(bytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(buffer.ReaderIndex, nioBuffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestSetZero()
    {
        buffer.Clear();

        while (buffer.IsWritable())
        {
            buffer.WriteByte((byte)0xFF);
        }

        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            buffer.SetZero(i, length);
            i += length;
        }

        for (int i = 0; i < buffer.Capacity; i++)
        {
            Assert.Equal(0, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestWriteZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteZero(-1));

        buffer.Clear();

        while (buffer.IsWritable())
        {
            buffer.WriteByte((byte)0xFF);
        }

        buffer.Clear();

        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            buffer.WriteZero(length);
            i += length;
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);

        for (int i = 0; i < buffer.Capacity; i++)
        {
            Assert.Equal(0, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestSetZeroAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetZero(0, 1);
        });
    }

    [Fact]
    public void TestWriteZeroAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteZero(1);
        });
    }

    [Fact]
    public void TestForEachByte()
    {
        buffer.Clear();

        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int lastIndex = 0;
        buffer.SetIndex(CAPACITY / 4, CAPACITY * 3 / 4);
        int processorIndex = CAPACITY / 4;
        Assert.Equal(-1, buffer.ForEachByte(value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            lastIndex = processorIndex;
            processorIndex++;
            return true;
        }));

        Assert.Equal(CAPACITY * 3 / 4 - 1, lastIndex);
    }

    [Fact]
    public void TestForEachByteAbort()
    {
        buffer.Clear();

        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int stop = CAPACITY / 2;
        int processorIndex = CAPACITY / 3;
        Assert.Equal(stop, buffer.ForEachByte(CAPACITY / 3, CAPACITY / 3, value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            if (processorIndex == stop)
            {
                return false;
            }

            processorIndex++;
            return true;
        }));
    }

    [Fact]
    public void TestForEachByteDesc()
    {
        buffer.Clear();

        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int lastIndex = 0;
        int processorIndex = CAPACITY * 3 / 4 - 1;
        Assert.Equal(-1, buffer.ForEachByteDesc(CAPACITY / 4, CAPACITY * 2 / 4, value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            lastIndex = processorIndex;
            processorIndex--;
            return true;
        }));

        Assert.Equal(CAPACITY / 4, lastIndex);
    }

    [Fact]
    public virtual void TestForEachByteDesc2()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4 };
        ByteBuf buf = NewBuffer(expected.Length);

        try
        {
            buf.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int processorIndex = bytes.Length - 1;
            int i = buf.ForEachByteDesc(value =>
            {
                bytes[processorIndex--] = value;
                return true;
            });
            Assert.Equal(-1, i);
            Assert.Equal(expected, bytes);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public virtual void TestForEachByte2()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4 };
        ByteBuf buf = NewBuffer(expected.Length);

        try
        {
            buf.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int processorIndex = 0;
            int i = buf.ForEachByte(value =>
            {
                bytes[processorIndex++] = value;
                return true;
            });
            Assert.Equal(-1, i);
            Assert.Equal(expected, bytes);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestForEachByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByte(_ => true);
                });
    }

    [Fact]
    public void TestForEachByteAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByte(0, 1, _ => true);
                });
    }

    [Fact]
    public void TestForEachByteDescAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByteDesc(_ => true);
                });
    }

    [Fact]
    public void TestForEachByteDescAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByteDesc(0, 1, _ => true);
                });
    }

    [Fact]
    public void TestWriteBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBoolean(true);
        });
    }

    [Fact]
    public void TestWriteByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteByte(1);
        });
    }

    [Fact]
    public void TestWriteShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteShort(1);
        });
    }

    [Fact]
    public void TestWriteShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteShortLE(1);
        });
    }

    [Fact]
    public void TestWriteMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteMedium(1);
        });
    }

    [Fact]
    public void TestWriteMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteMediumLE(1);
        });
    }

    [Fact]
    public void TestWriteIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteInt(1);
        });
    }

    [Fact]
    public void TestWriteIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteIntLE(1);
        });
    }

    [Fact]
    public void TestWriteLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteLong(1);
        });
    }

    [Fact]
    public void TestWriteLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteLongLE(1);
        });
    }

    [Fact]
    public void TestWriteCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteChar((char)1);
        });
    }

    [Fact]
    public void TestWriteFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteFloat(1);
        });
    }

    [Fact]
    public void TestWriteFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteFloatLE(1);
        });
    }

    [Fact]
    public void TestWriteDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteDouble(1);
        });
    }

    [Fact]
    public void TestWriteDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteDoubleLE(1);
        });
    }

    protected void AssertOriginalNioBuffer1()
    {
        Assert.True(buffer.IsContiguous);
        byte[] value = new byte[buffer.Capacity];
        random.NextBytes(value);
        buffer.Clear();
        buffer.WriteBytes(value);
        AssertRemainingEquals(value, buffer.ReadableMemory);
    }

    protected void AssertOriginalToByteBuffer2()
    {
        Assert.True(buffer.IsContiguous);
        byte[] value = new byte[buffer.Capacity];
        random.NextBytes(value);
        buffer.Clear();
        buffer.WriteBytes(value);

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
            AssertRemainingEquals(value.AsSpan(i, BLOCK_SIZE), buffer.AsReadOnlyMemory(i, BLOCK_SIZE));
    }

    protected void AssertOriginalToByteBuffer3()
    {
        // CLR: AbstractContiguousByteBufTest selects the providers of assumeTrue(buffer.nioBufferCount() == 1).
        Assert.True(buffer.IsContiguous);

        // CLR: neither ByteBuf nor ReadOnlyMemory has a byte-order property. Explicit BE/LE reads of the
        // borrowed readable region must agree with the buffer's own explicit BE/LE accessors.
        buffer.Clear().WriteInt(0x01020304);
        ReadOnlySpan<byte> region = buffer.ReadableMemory.Span;
        Assert.Equal(buffer.GetInt(0), BinaryPrimitives.ReadInt32BigEndian(region));
        Assert.Equal(buffer.GetIntLE(0), BinaryPrimitives.ReadInt32LittleEndian(region));
    }

    private static void AssertRemainingEquals(ReadOnlySpan<byte> expected, ReadOnlyMemory<byte> actual)
    {
        int remaining = expected.Length;
        int remaining2 = actual.Length;
        Assert.Equal(remaining, remaining2);
        byte[] array1 = new byte[remaining];
        byte[] array2 = new byte[remaining2];
        expected.CopyTo(array1);
        actual.Span.CopyTo(array2);
        Assert.Equal(array1, array2);
    }

    protected void AssertOriginalInternalNioBufferGrowth()
    {
        AssertInternalBorrowAfterGrowth(128);
        AssertInternalBorrowAfterGrowth(1024);
        AssertInternalBorrowAfterGrowth(4 * 1024);
        AssertInternalBorrowAfterGrowth(64 * 1024);
        AssertInternalBorrowAfterGrowth(32 * 1024 * 1024);
        AssertInternalBorrowAfterGrowth(64 * 1024 * 1024);
    }

    private void AssertInternalBorrowAfterGrowth(int a)
    {
        ByteBuf buffer = NewBuffer(2);

        try
        {
            ReadOnlyMemory<byte> buf = buffer.AsReadOnlyMemory(buffer.ReaderIndex, 1);
            Assert.Equal(1, buf.Length);
            byte[] data = new byte[a];
            Random.Shared.NextBytes(data);
            buffer.WriteBytes(data);

            // Reacquire after growth. A prior CLR borrow must not be used across resize.
            buf = buffer.AsReadOnlyMemory(buffer.ReaderIndex, a);
            Assert.Equal(a, buf.Length);
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(buf));

            for (int i = 0; i < a; i++)
            {
                byte actual = reader.UnreadSpan[0];
                reader.Advance(1);

                // Preserve every original byte comparison and first-failure order;
                // avoid generic assertion dispatch for hundreds of millions of equal bytes.
                if (data[i] != actual)
                {
                    Assert.Equal(data[i], actual);
                }
            }

            Assert.True(reader.End);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public virtual void TestGetReadOnlyDirectDst()
    {
        AssertGetReadOnlyDst(true);
    }

    [Fact]
    public virtual void TestGetReadOnlyHeapDst()
    {
        AssertGetReadOnlyDst(false);
    }

    private void AssertGetReadOnlyDst(bool direct)
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d' };
        ByteBuf buffer = NewBuffer(bytes.Length);

        try
        {
            buffer.WriteBytes(bytes);
            ByteBuf dst = direct ? DirectBuffer(bytes.Length) : Own(_peerAllocator.HeapBuffer(bytes.Length));
            dst.SetZero(0, bytes.Length);
            ByteBuf readOnlyDst = dst.AsReadOnly();
            // CLR Span rejects ReadOnlySpan at compile time. ByteBuf destinations
            // additionally preserve the original dynamic write-permission rejection.
            Assert.Throws<NotSupportedException>(() => buffer.GetBytes(0, readOnlyDst));
            Assert.Equal(0, readOnlyDst.WriterIndex);
            Assert.Equal(new byte[bytes.Length], ByteBufUtil.GetBytes(dst, 0, bytes.Length));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestReadyOnlyNioBuffer()
    {
        AssertReadyOnlyNioBuffer(buffer.AsReadOnly());
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffer()
    {
        AssertReadyOnlyNioBuffer(buffer.AsReadOnly().Slice());
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffer()
    {
        AssertReadyOnlyNioBuffer(buffer.AsReadOnly().Duplicate());
    }

    private static void AssertReadyOnlyNioBuffer(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        // ReadOnlyMemory/ReadOnlySpan carry write prohibition in their types, not an isReadOnly flag.
        ReadOnlyMemory<byte> memory = buffer.ReadableMemory;
        Assert.Equal(buffer.ReadableBytes, memory.Length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestReadyOnlyNioBufferWithPositionLength()
    {
        AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly());
    }

    [Fact]
    public void TestReadyOnlySliceNioBufferWithPositionLength()
    {
        AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly().Slice());
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBufferWithPositionLength()
    {
        AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly().Duplicate());
    }

    private static void AssertReadyOnlyNioBufferWithPositionLength(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        // CLR contiguous borrows never flatten composite storage. Use the sequence contract for that range.
        long length = buffer.IsContiguous ? buffer.AsReadOnlyMemory(0, buffer.Capacity).Length
            : buffer.AsReadOnlySequence(0, buffer.Capacity).Length;
        Assert.Equal(buffer.Capacity, length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, buffer.Capacity));
    }

    [Fact]
    public void TestReadyOnlyNioBuffers()
    {
        AssertReadyOnlyNioBuffers(buffer.AsReadOnly());
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffers()
    {
        AssertReadyOnlyNioBuffers(buffer.AsReadOnly().Slice());
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffers()
    {
        AssertReadyOnlyNioBuffers(buffer.AsReadOnly().Duplicate());
    }

    private static void AssertReadyOnlyNioBuffers(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        ReadOnlySequence<byte> sequence = buffer.ReadableSequence;
        Assert.Equal(buffer.ReadableBytes, sequence.Length);
        // Each segment is statically ReadOnlyMemory<byte>; compiler probes check its write prohibition.
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestReadyOnlyNioBuffersWithPositionLength()
    {
        AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly());
    }

    [Fact]
    public void TestReadyOnlySliceNioBuffersWithPositionLength()
    {
        AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly().Slice());
    }

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffersWithPositionLength()
    {
        AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly().Duplicate());
    }

    private static void AssertReadyOnlyNioBuffersWithPositionLength(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        ReadOnlySequence<byte> sequence = buffer.AsReadOnly().AsReadOnlySequence(0, buffer.Capacity);
        Assert.Equal(buffer.Capacity, sequence.Length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, buffer.Capacity));
    }

    [Fact]
    public void Test_internalNioBuffer()
    {
        ByteBuf buf = Unpooled.Buffer(8, 16);

        // CLR: Unpooled.Buffer never returns a SimpleLeakAwareByteBuf here and every CLR ByteBuf
        // derives from ByteBuf, so the original unwrap and AbstractByteBuf assumption always hold.
        try
        {
            // CLR: _internalNioBuffer exposes capacity (internalNioBuffer(0, capacity)), not just the
            // currently empty readable region; a bounded Memory and SequenceReader replace the ByteBuffer.
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(buf.AsReadOnlyMemory(0, buf.Capacity)));
            Assert.Equal(0, reader.Consumed);
            Assert.True(reader.Remaining >= 8);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void EnsureWritableWithForceDoesNotThrow()
    {
        AssertEnsureWritableDoesNotThrow(true);
    }

    [Fact]
    public void EnsureWritableWithOutForceDoesNotThrow()
    {
        AssertEnsureWritableDoesNotThrow(false);
    }

    [Fact]
    public void EnsureWritableWithForceAsReadyOnly()
    {
        AssertEnsureWritableReadOnly(true);
    }

    [Fact]
    public void EnsureWritableWithOutForceAsReadOnly()
    {
        AssertEnsureWritableReadOnly(false);
    }

    [Fact]
    public void TestEnsureWritableAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().EnsureWritable(16);
        });
    }

    [Fact]
    public void TestEnsureWritableIntegerOverflow()
    {
        ByteBuf buffer = NewBuffer(CAPACITY);
        buffer.WriterIndex = buffer.ReaderIndex;
        buffer.WriteByte(1);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(int.MaxValue));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestGetBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBoolean(0);
        });
    }

    [Fact]
    public void TestGetByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetByte(0);
        });
    }

    [Fact]
    public void TestGetUnsignedByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetByte(0);
        });
    }

    [Fact]
    public void TestGetShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetShort(0);
        });
    }

    [Fact]
    public void TestGetShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetShortLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedShort(0);
        });
    }

    [Fact]
    public void TestGetUnsignedShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedShortLE(0);
        });
    }

    [Fact]
    public void TestGetMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetMedium(0);
        });
    }

    [Fact]
    public void TestGetMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetMediumLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedMedium(0);
        });
    }

    [Fact]
    public void TestGetIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetInt(0);
        });
    }

    [Fact]
    public void TestGetIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetIntLE(0);
        });
    }

    [Fact]
    public void TestGetUnsignedIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedInt(0);
        });
    }

    [Fact]
    public void TestGetUnsignedIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetUnsignedIntLE(0);
        });
    }

    [Fact]
    public void TestGetLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetLong(0);
        });
    }

    [Fact]
    public void TestGetLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetLongLE(0);
        });
    }

    [Fact]
    public void TestGetCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetChar(0);
        });
    }

    [Fact]
    public void TestGetFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetFloat(0);
        });
    }

    [Fact]
    public void TestGetFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetFloatLE(0);
        });
    }

    [Fact]
    public void TestGetDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetDouble(0);
        });
    }

    [Fact]
    public void TestGetDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetDoubleLE(0);
        });
    }

    [Fact]
    public void TestSetBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBoolean(0, true);
        });
    }

    [Fact]
    public void TestSetByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetByte(0, 1);
        });
    }

    [Fact]
    public void TestSetShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetShort(0, 1);
        });
    }

    [Fact]
    public void TestSetShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetShortLE(0, 1);
        });
    }

    [Fact]
    public void TestSetMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetMedium(0, 1);
        });
    }

    [Fact]
    public void TestSetMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetMediumLE(0, 1);
        });
    }

    [Fact]
    public void TestSetIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetInt(0, 1);
        });
    }

    [Fact]
    public void TestSetIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetIntLE(0, 1);
        });
    }

    [Fact]
    public void TestSetLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetLong(0, 1);
        });
    }

    [Fact]
    public void TestSetLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetLongLE(0, 1);
        });
    }

    [Fact]
    public void TestSetCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetChar(0, (char)1);
        });
    }

    [Fact]
    public void TestSetFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetFloat(0, 1);
        });
    }

    [Fact]
    public void TestSetDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetDouble(0, 1);
        });
    }

    [Fact]
    public void TestReadBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBoolean();
        });
    }

    [Fact]
    public void TestReadByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadByte();
        });
    }

    [Fact]
    public void TestReadUnsignedByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadByte();
        });
    }

    [Fact]
    public void TestReadShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadShort();
        });
    }

    [Fact]
    public void TestReadShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadShortLE();
        });
    }

    [Fact]
    public void TestReadUnsignedShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedShort();
        });
    }

    [Fact]
    public void TestReadUnsignedShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedShortLE();
        });
    }

    [Fact]
    public void TestReadMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadMedium();
        });
    }

    [Fact]
    public void TestReadMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadMediumLE();
        });
    }

    [Fact]
    public void TestReadUnsignedMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedMedium();
        });
    }

    [Fact]
    public void TestReadUnsignedMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedMediumLE();
        });
    }

    [Fact]
    public void TestReadIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadInt();
        });
    }

    [Fact]
    public void TestReadIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadIntLE();
        });
    }

    [Fact]
    public void TestReadUnsignedIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedInt();
        });
    }

    [Fact]
    public void TestReadUnsignedIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadUnsignedIntLE();
        });
    }

    [Fact]
    public void TestReadLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadLong();
        });
    }

    [Fact]
    public void TestReadLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadLongLE();
        });
    }

    [Fact]
    public void TestReadCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadChar();
        });
    }

    [Fact]
    public void TestReadFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadFloat();
        });
    }

    [Fact]
    public void TestReadFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadFloatLE();
        });
    }

    [Fact]
    public void TestReadDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadDouble();
        });
    }

    [Fact]
    public void TestReadDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadDoubleLE();
        });
    }

    [Fact]
    public void GetBooleanBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBoolean(-1);
        });
    }

    [Fact]
    public void GetBooleanBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBoolean(buffer.Capacity);
        });
    }

    [Fact]
    public void GetByteBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetByte(-1);
        });
    }

    [Fact]
    public void GetByteBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetByte(buffer.Capacity);
        });
    }

    [Fact]
    public void GetShortBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetShort(-1);
        });
    }

    [Fact]
    public void GetShortBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetShort(buffer.Capacity - 1);
        });
    }

    [Fact]
    public void GetMediumBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetMedium(-1);
        });
    }

    [Fact]
    public void GetMediumBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetMedium(buffer.Capacity - 2);
        });
    }

    [Fact]
    public void GetIntBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetInt(-1);
        });
    }

    [Fact]
    public void GetIntBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetInt(buffer.Capacity - 3);
        });
    }

    [Fact]
    public void GetLongBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetLong(-1);
        });
    }

    [Fact]
    public void GetLongBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetLong(buffer.Capacity - 7);
        });
    }

    [Fact]
    public void ReadByteThrowsIndexOutOfBoundsException()
    {
        ByteBuf buffer = NewBuffer(8);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.WriteByte(0);
                Assert.Equal((byte)0, buffer.ReadByte());
                buffer.ReadByte();
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    private void AssertEnsureWritableDoesNotThrow(bool force)
    {
        ByteBuf buffer = NewBuffer(8);
        buffer.WriterIndex = buffer.Capacity;
        buffer.EnsureWritable(8, force);
        buffer.Release();
    }

    private void AssertEnsureWritableReadOnly(bool force)
    {
        ByteBuf buffer = NewBuffer(8);
        buffer.WriterIndex = buffer.Capacity;
        Assert.Equal(1, buffer.AsReadOnly().EnsureWritable(8, force));
        buffer.Release();
    }

    [Fact]
    public void TestRandomByteAccess()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(value, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestRandomUnsignedByteAccess()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            int value = NextSignedInt32() & 0xFF;
            Assert.Equal(value, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestRandomShortAccess()
    {
        AssertRandomShortAccess(true);
    }

    [Fact]
    public void TestRandomShortLEAccess()
    {
        AssertRandomShortAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedShortAccess()
    {
        AssertRandomUnsignedShortAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedShortLEAccess()
    {
        AssertRandomUnsignedShortAccess(false);
    }

    [Fact]
    public void TestRandomMediumAccess()
    {
        AssertRandomMediumAccess(true);
    }

    [Fact]
    public void TestRandomMediumLEAccess()
    {
        AssertRandomMediumAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedMediumAccess()
    {
        AssertRandomUnsignedMediumAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedMediumLEAccess()
    {
        AssertRandomUnsignedMediumAccess(false);
    }

    [Fact]
    public void TestRandomIntAccess()
    {
        AssertRandomIntAccess(true);
    }

    [Fact]
    public void TestRandomIntLEAccess()
    {
        AssertRandomIntAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedIntAccess()
    {
        AssertRandomUnsignedIntAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedIntLEAccess()
    {
        AssertRandomUnsignedIntAccess(false);
    }

    [Fact]
    public void TestRandomLongAccess()
    {
        AssertRandomLongAccess(true);
    }

    [Fact]
    public void TestRandomLongLEAccess()
    {
        AssertRandomLongAccess(false);
    }

    [Fact]
    public void TestRandomFloatAccess()
    {
        AssertRandomFloatAccess(true);
    }

    [Fact]
    public void TestRandomFloatLEAccess()
    {
        AssertRandomFloatAccess(false);
    }

    [Fact]
    public void TestRandomDoubleAccess()
    {
        AssertRandomDoubleAccess(true);
    }

    [Fact]
    public void TestRandomDoubleLEAccess()
    {
        AssertRandomDoubleAccess(false);
    }

    [Fact]
    public void TestSequentialByteAccess()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());
            buffer.WriteByte(value);
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());
            Assert.Equal(value, buffer.ReadByte());
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    [Fact]
    public void TestSequentialUnsignedByteAccess()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());
            buffer.WriteByte(value);
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i ++)
        {
            int value = NextSignedInt32() & 0xFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());
            Assert.Equal(value, buffer.ReadByte());
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    [Fact]
    public void TestSequentialShortAccess()
    {
        AssertSequentialShortAccess(true);
    }

    [Fact]
    public void TestSequentialShortLEAccess()
    {
        AssertSequentialShortAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedShortAccess()
    {
        AssertSequentialUnsignedShortAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedShortLEAccess()
    {
        AssertSequentialUnsignedShortAccess(true);
    }

    [Fact]
    public void TestSequentialMediumAccess()
    {
        AssertSequentialMediumAccess(true);
    }

    [Fact]
    public void TestSequentialMediumLEAccess()
    {
        AssertSequentialMediumAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedMediumAccess()
    {
        AssertSequentialUnsignedMediumAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedMediumLEAccess()
    {
        AssertSequentialUnsignedMediumAccess(false);
    }

    [Fact]
    public void TestSequentialIntAccess()
    {
        AssertSequentialIntAccess(true);
    }

    [Fact]
    public void TestSequentialIntLEAccess()
    {
        AssertSequentialIntAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedIntAccess()
    {
        AssertSequentialUnsignedIntAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedIntLEAccess()
    {
        AssertSequentialUnsignedIntAccess(false);
    }

    [Fact]
    public void TestSequentialLongAccess()
    {
        AssertSequentialLongAccess(true);
    }

    [Fact]
    public void TestSequentialLongLEAccess()
    {
        AssertSequentialLongAccess(false);
    }

    private void AssertRandomShortAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());

            if (testBigEndian)
            {
                buffer.SetShort(i, value);
            }
            else
            {
                buffer.SetShortLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetShort(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetShortLE(i));
            }
        }
    }

    private void AssertRandomUnsignedShortAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());

            if (testBigEndian)
            {
                buffer.SetShort(i, value);
            }
            else
            {
                buffer.SetShortLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            int value = NextSignedInt32() & 0xFFFF;

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedShort(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedShortLE(i));
            }
        }
    }

    private void AssertRandomMediumAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32();

            if (testBigEndian)
            {
                buffer.SetMedium(i, value);
            }
            else
            {
                buffer.SetMediumLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32() << 8 >> 8;

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetMedium(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetMediumLE(i));
            }
        }
    }

    private void AssertRandomUnsignedMediumAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32();

            if (testBigEndian)
            {
                buffer.SetMedium(i, value);
            }
            else
            {
                buffer.SetMediumLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedMedium(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedMediumLE(i));
            }
        }
    }

    private void AssertRandomIntAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();

            if (testBigEndian)
            {
                buffer.SetInt(i, value);
            }
            else
            {
                buffer.SetIntLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetInt(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetIntLE(i));
            }
        }
    }

    private void AssertRandomUnsignedIntAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();

            if (testBigEndian)
            {
                buffer.SetInt(i, value);
            }
            else
            {
                buffer.SetIntLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            long value = NextSignedInt32() & 0xFFFFFFFFL;

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedInt(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedIntLE(i));
            }
        }
    }

    private void AssertRandomLongAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            long value = NextSignedInt64();

            if (testBigEndian)
            {
                buffer.SetLong(i, value);
            }
            else
            {
                buffer.SetLongLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            long value = NextSignedInt64();

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetLong(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetLongLE(i));
            }
        }
    }

    private void AssertRandomFloatAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            float value = random.NextSingle();

            if (testBigEndian)
            {
                buffer.SetFloat(i, value);
            }
            else
            {
                buffer.SetFloatLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            float expected = random.NextSingle();
            float actual = testBigEndian? buffer.GetFloat(i) : buffer.GetFloatLE(i);
            Assert.Equal((double)expected, (double)actual, 0.01);
        }
    }

    private void AssertRandomDoubleAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            double value = random.NextDouble();

            if (testBigEndian)
            {
                buffer.SetDouble(i, value);
            }
            else
            {
                buffer.SetDoubleLE(i, value);
            }
        }

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            double expected = random.NextDouble();
            double actual = testBigEndian? buffer.GetDouble(i) : buffer.GetDoubleLE(i);
            Assert.Equal(expected, actual, 0.01);
        }
    }

    private void AssertSequentialShortAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteShort(value);
            }
            else
            {
                buffer.WriteShortLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadShort());
            }
            else
            {
                Assert.Equal(value, buffer.ReadShortLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    private void AssertSequentialUnsignedShortAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteShort(value);
            }
            else
            {
                buffer.WriteShortLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            int value = NextSignedInt32() & 0xFFFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedShort());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedShortLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    private void AssertSequentialMediumAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteMedium(value);
            }
            else
            {
                buffer.WriteMediumLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() << 8 >> 8;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadMedium());
            }
            else
            {
                Assert.Equal(value, buffer.ReadMediumLE());
            }
        }

        Assert.Equal(buffer.Capacity / 3 * 3, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReadableBytes);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);
    }

    private void AssertSequentialUnsignedMediumAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteMedium(value);
            }
            else
            {
                buffer.WriteMediumLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedMedium());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedMediumLE());
            }
        }

        Assert.Equal(buffer.Capacity / 3 * 3, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReadableBytes);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);
    }

    private void AssertSequentialIntAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteInt(value);
            }
            else
            {
                buffer.WriteIntLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadInt());
            }
            else
            {
                Assert.Equal(value, buffer.ReadIntLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    private void AssertSequentialUnsignedIntAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteInt(value);
            }
            else
            {
                buffer.WriteIntLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            long value = NextSignedInt32() & 0xFFFFFFFFL;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedInt());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedIntLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    private void AssertSequentialLongAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity; i += 8)
        {
            long value = NextSignedInt64();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable());

            if (testBigEndian)
            {
                buffer.WriteLong(value);
            }
            else
            {
                buffer.WriteLongLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable());

        random = new Random(seed);

        for (int i = 0; i < buffer.Capacity; i += 8)
        {
            long value = NextSignedInt64();
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable());

            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadLong());
            }
            else
            {
                Assert.Equal(value, buffer.ReadLongLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable());
        Assert.False(buffer.IsWritable());
    }

    [Fact]
    public void TestSWARIndexOf()
    {
        ByteBuf buffer = NewBuffer(16);
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);
        buffer.WriteByte((byte)0); // 0
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0); // 7

        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)1); // 11
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)4);
        buffer.WriteByte((byte)1); // 15
        Assert.Equal(11, buffer.IndexOf(0, 12, (byte)1));
        Assert.Equal(12, buffer.IndexOf(0, 16, (byte)2));
        Assert.Equal(-1, buffer.IndexOf(0, 11, (byte)1));
        Assert.Equal(11, buffer.IndexOf(0, 16, (byte)1));

        // lastIndexOf
        Assert.Equal(15, buffer.IndexOf(16, 0, (byte)1));
        Assert.Equal(12, buffer.IndexOf(16, 0, (byte)2));
        Assert.Equal(11, buffer.IndexOf(15, 0, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(11, 0, (byte)1));
        buffer.Release();
    }

    [Fact]
    public void TestUnrolledSWARIndexOf()
    {
        ByteBuf buffer = NewBuffer(15);
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);
        buffer.WriteByte((byte)0); // 0
        buffer.WriteByte((byte)1);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)4);
        buffer.WriteByte((byte)5);
        buffer.WriteByte((byte)6);
        buffer.WriteByte((byte)7); // 7

        buffer.WriteByte((byte)8); // 8
        buffer.WriteByte((byte)9);
        buffer.WriteByte((byte)10);
        buffer.WriteByte((byte)11);
        buffer.WriteByte((byte)12);
        buffer.WriteByte((byte)13);
        buffer.WriteByte((byte)14); // 14
        Assert.Equal(15, buffer.Capacity);

        for (int i = 0; i < 14; ++i)
        {
            Assert.Equal(i, buffer.IndexOf(i, buffer.Capacity, (byte)i));
        }

        // lastIndexOf
        for (int i = 0; i < 14; ++i)
        {
            Assert.Equal(i, buffer.IndexOf(buffer.Capacity, 0, (byte)i));
        }

        buffer.Release();
    }

    [Fact]
    public void TestIndexOf()
    {
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);

        buffer.WriteByte((byte)1);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)1);

        Assert.Equal(-1, buffer.IndexOf(1, 4, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(4, 1, (byte)1));
        Assert.Equal(1, buffer.IndexOf(1, 4, (byte)2));
        Assert.Equal(3, buffer.IndexOf(4, 1, (byte)2));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(0, buffer.Capacity + 1, (byte)0));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(buffer.Capacity, -1, (byte)0));

        Assert.Equal(4, buffer.IndexOf(buffer.Capacity + 1, 0, (byte)1));
        Assert.Equal(0, buffer.IndexOf(-1, buffer.Capacity, (byte)1));
    }

    [Fact]
    public void TestIndexOfReleaseBuffer()
    {
        ByteBuf buffer = ReleasedBuffer();
        if (buffer.Capacity != 0)
        {
            Assert.Throws<IllegalReferenceCountException>(() => buffer.IndexOf(0, 1, (byte)1));
        }
        else
        {
            Assert.Equal(-1, buffer.IndexOf(0, 1, (byte)1));
        }
    }

    [Fact]
    public void TestEndiannessIndexOf()
    {
        buffer.Clear();
        int v = 0x02030201;
        buffer.WriteIntLE(v);
        buffer.WriteByte(0x01);

        Assert.Equal(-1, buffer.IndexOf(1, 4, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(4, 1, (byte)1));
        Assert.Equal(1, buffer.IndexOf(1, 4, (byte)2));
        Assert.Equal(3, buffer.IndexOf(4, 1, (byte)2));
    }

    [Fact]
    public void TestEquals()
    {
        // CLR has fixed explicit BE/LE accessors; Java-only mutable-order variants are recorded separately.
        Assert.False(buffer.Equals((object)null));
        Assert.False(buffer.Equals(new object()));

        byte[] value = new byte[32];
        buffer.SetIndex(0, value.Length);
        random.NextBytes(value);
        buffer.SetBytes(0, value);

        Assert.Equal(buffer, WrappedBuffer(value));

        value[0] = unchecked((byte)(value[0] + 1));
        Assert.False(buffer.Equals(WrappedBuffer(value)));
    }

    [Fact]
    public void TestCompareTo()
    {
        // CLR has fixed explicit BE/LE accessors; Java-only mutable-order variants are recorded separately.
        Assert.Equal(1, buffer.CompareTo(null));

        // Fill the random stuff
        byte[] value = new byte[32];
        random.NextBytes(value);

        // Prevent overflow / underflow
        if (value[0] == 0)
        {
            value[0] = unchecked((byte)(value[0] + 1));
        }
        else if (value[0] == byte.MaxValue)
        {
            value[0]--;
        }

        buffer.SetIndex(0, value.Length);
        buffer.SetBytes(0, value);

        Assert.Equal(0, buffer.CompareTo(WrappedBuffer(value)));

        value[0] = unchecked((byte)(value[0] + 1));
        Assert.True(buffer.CompareTo(WrappedBuffer(value)) < 0);
        value[0] -= 2;
        Assert.True(buffer.CompareTo(WrappedBuffer(value)) > 0);
        value[0] = unchecked((byte)(value[0] + 1));

        Assert.True(buffer.CompareTo(Own(Unpooled.WrappedBuffer(value, 0, 31))) > 0);
        Assert.True(buffer.Slice(0, 31).CompareTo(WrappedBuffer(value)) < 0);

        ByteBuf retainedSlice = buffer.RetainedSlice(0, 31);
        Assert.True(retainedSlice.CompareTo(WrappedBuffer(value)) < 0);
        retainedSlice.Release();
    }

    [Fact]
    public void InitialState()
    {
        Assert.Equal(CAPACITY, buffer.Capacity);
        Assert.Equal(0, buffer.ReaderIndex);
    }

    [Fact]
    public void ComparableInterfaceNotViolated()
    {
        Assert.False(buffer.IsReadOnly);
        buffer.WriterIndex = buffer.ReaderIndex;
        Assert.True(buffer.WritableBytes >= 4);

        buffer.WriteLong(0);
        ByteBuf buffer2 = NewBuffer(CAPACITY);
        Assert.False(buffer2.IsReadOnly);
        buffer2.WriterIndex = buffer2.ReaderIndex;
        // Write an unsigned integer that will cause buffer.getUnsignedInt() - buffer2.getUnsignedInt() to underflow the
        // int type and wrap around on the negative side.
        buffer2.WriteLong(0xF0000000L);
        Assert.True(buffer.CompareTo(buffer2) < 0);
        Assert.True(buffer2.CompareTo(buffer) > 0);
        buffer2.Release();
    }

    [Fact]
    public void CopyBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(-1, 0);
        });
    }

    [Fact]
    public void CopyBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(0, buffer.Capacity + 1);
        });
    }

    [Fact]
    public void CopyBoundaryCheck3()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(buffer.Capacity + 1, 0);
        });
    }

    [Fact]
    public void CopyBoundaryCheck4()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(buffer.Capacity, 1);
        });
    }

    [Fact]
    public void TestCopy()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        int readerIndex = CAPACITY / 3;
        int writerIndex = CAPACITY * 2 / 3;
        buffer.SetIndex(readerIndex, writerIndex);

        // Make sure all properties are copied.
        ByteBuf copy = Own(buffer.Copy());
        Assert.Equal(0, copy.ReaderIndex);
        Assert.Equal(buffer.ReadableBytes, copy.WriterIndex);
        Assert.Equal(buffer.ReadableBytes, copy.Capacity);

        // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
        for (int i = 0; i < copy.Capacity; i ++)
        {
            Assert.Equal(buffer.GetByte(i + readerIndex), copy.GetByte(i));
        }

        // Make sure the buffer content is independent from each other.
        buffer.SetByte(readerIndex, unchecked((byte)(buffer.GetByte(readerIndex) + 1)));
        Assert.True(buffer.GetByte(readerIndex) != copy.GetByte(0));
        copy.SetByte(1, unchecked((byte)(copy.GetByte(1) + 1)));
        Assert.True(buffer.GetByte(readerIndex + 1) != copy.GetByte(1));
        copy.Release();
    }

    [Fact]
    public void TestDuplicate()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        int readerIndex = CAPACITY / 3;
        int writerIndex = CAPACITY * 2 / 3;
        buffer.SetIndex(readerIndex, writerIndex);

        // Make sure all properties are copied.
        ByteBuf duplicate = buffer.Duplicate();
        // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
        Assert.Equal(buffer.ReadableBytes, duplicate.ReadableBytes);
        Assert.Equal(0, buffer.CompareTo(duplicate));

        // Make sure the buffer content is shared.
        buffer.SetByte(readerIndex, unchecked((byte)(buffer.GetByte(readerIndex) + 1)));
        Assert.Equal(buffer.GetByte(readerIndex), duplicate.GetByte(duplicate.ReaderIndex));
        duplicate.SetByte(duplicate.ReaderIndex, unchecked((byte)(duplicate.GetByte(duplicate.ReaderIndex) + 1)));
        Assert.Equal(buffer.GetByte(readerIndex), duplicate.GetByte(duplicate.ReaderIndex));
    }

    // Keep the original HashSet.Contains and Count operations rather than collection enumeration assertions.
#pragma warning disable xUnit2013, xUnit2017
    [Fact]
    public void TestHashCode()
    {
        ByteBuf elemA = Own(_peerAllocator.HeapBuffer(15));
        ByteBuf elemB = DirectBuffer(15);
        elemA.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5 });
        elemB.WriteBytes(new byte[] { 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 });

        HashSet<ByteBuf> set = new HashSet<ByteBuf>();
        set.Add(elemA);
        set.Add(elemB);

        Assert.Equal(2, set.Count);
        ByteBuf elemACopy = Own(elemA.Copy());
        Assert.True(set.Contains(elemACopy));

        ByteBuf elemBCopy = Own(elemB.Copy());
        Assert.True(set.Contains(elemBCopy));

        buffer.Clear();
        buffer.WriteBytes(elemA.Duplicate());

        Assert.True(set.Remove(buffer));
        Assert.False(set.Contains(elemA));
        Assert.Equal(1, set.Count);

        buffer.Clear();
        buffer.WriteBytes(elemB.Duplicate());
        Assert.True(set.Remove(buffer));
        Assert.False(set.Contains(elemB));
        Assert.Equal(0, set.Count);
        elemA.Release();
        elemB.Release();
        elemACopy.Release();
        elemBCopy.Release();
    }
#pragma warning restore xUnit2013, xUnit2017

    [Fact]
    public void TestCopyAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Copy();
        });
    }

    [Fact]
    public void TestCopyAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Copy();
        });
    }

    [Fact]
    public void TestReadOnlyRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.RefCnt);
        Assert.True(buf.AsReadOnly().Release());
        Assert.Equal(0, buf.RefCnt);
    }

    // CLR: string and ReadOnlySpan<char> replace CharBuffer/CharSequence; ReadString serves both
    // readCharSequence and readString. CLR Encoding writes the exact byte length without an automatic
    // BOM, so Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16
    // (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.bigendianunicode?view=net-10.0).
    // ByteBufTextSourceTest keeps the additional five-provider CLR wire/cursor checks.
    // CLR: Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16 without its BOM.
    private static readonly Encoding UTF_16 = Encoding.BigEndianUnicode;

    [Fact]
    public void TestToString()
    {
        ByteBuf copied = Unpooled.CopiedBuffer("Hello, World!", Encoding.Latin1);
        buffer.Clear();
        buffer.WriteBytes(copied);
        Assert.Equal("Hello, World!", buffer.ToString(Encoding.Latin1));
        copied.Release();
    }

    [Fact]
    public void TestSetUsAsciiCharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestSetIso88591CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestSetUtf8CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestSetUtf16CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(UTF_16);
        });
    }

    private void TestSetCharSequenceAfterRelease0(Encoding charset)
    {
        ReleasedBuffer().SetCharSequence(0, "x", charset);
    }

    [Fact]
    public void TestWriteUsAsciiCharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestWriteIso88591CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestWriteUtf8CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestWriteUtf16CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(UTF_16);
        });
    }

    private void TestWriteCharSequenceAfterRelease0(Encoding charset)
    {
        ReleasedBuffer().WriteCharSequence("x", charset);
    }

    [Fact]
    public virtual void TestWriteUsAsciiCharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.ASCII);
    }

    [Fact]
    public virtual void TestWriteUtf8CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.UTF8);
    }

    [Fact]
    public virtual void TestWriteIso88591CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.Latin1);
    }

    [Fact]
    public virtual void TestWriteUtf16CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(UTF_16);
    }

    private void TestWriteCharSequenceExpand(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1);

        try
        {
            int writerIndex = buf.Capacity - 1;
            buf.WriterIndex = writerIndex;
            int written = buf.WriteCharSequence("AB", charset);
            Assert.Equal(writerIndex, buf.WriterIndex - written);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetUsAsciiCharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestSetUtf8CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestSetIso88591CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestSetUtf16CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(UTF_16);
        });
    }

    private void TestSetCharSequenceNoExpand(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1);

        try
        {
            buf.SetCharSequence(0, "AB", charset);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetUsAsciiCharSequence()
    {
        TestSetGetCharSequence(Encoding.ASCII);
    }

    [Fact]
    public void TestSetUtf8CharSequence()
    {
        TestSetGetCharSequence(Encoding.UTF8);
    }

    [Fact]
    public void TestSetIso88591CharSequence()
    {
        TestSetGetCharSequence(Encoding.Latin1);
    }

    [Fact]
    public void TestSetUtf16CharSequence()
    {
        TestSetGetCharSequence(UTF_16);
    }

    private static readonly string EXTENDED_ASCII_CHARS, ASCII_CHARS;

    static AbstractByteBufTest()
    {
        char[] chars = new char[256];

        for (char c = (char)0; c < chars.Length; c++)
        {
            chars[c] = c;
        }

        EXTENDED_ASCII_CHARS = new string(chars);
        ASCII_CHARS = new string(chars, 0, 128);
    }

    private void TestSetGetCharSequence(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        int bytes = buf.SetCharSequence(1, sequence, charset);
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.GetCharSequence(1, bytes, charset).ToString());
        buf.Release();
    }

    [Fact]
    public void TestWriteReadUsAsciiCharSequence()
    {
        TestWriteReadCharSequence(Encoding.ASCII);
    }

    [Fact]
    public void TestWriteReadUtf8CharSequence()
    {
        TestWriteReadCharSequence(Encoding.UTF8);
    }

    [Fact]
    public void TestWriteReadIso88591CharSequence()
    {
        TestWriteReadCharSequence(Encoding.Latin1);
    }

    [Fact]
    public void TestWriteReadUtf16CharSequence()
    {
        TestWriteReadCharSequence(UTF_16);
    }

    private void TestWriteReadCharSequence(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.ReadCharSequence(bytes, charset).ToString());
        buf.Release();
    }

    [Fact]
    public void TestWriteReadUsAsciiString()
    {
        TestWriteReadString(Encoding.ASCII);
    }

    [Fact]
    public void TestWriteReadUtf8String()
    {
        TestWriteReadString(Encoding.UTF8);
    }

    [Fact]
    public void TestWriteReadIso88591String()
    {
        TestWriteReadString(Encoding.Latin1);
    }

    [Fact]
    public void TestWriteReadUtf16String()
    {
        TestWriteReadString(UTF_16);
    }

    private void TestWriteReadString(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        Assert.Equal(sequence, buf.ReadString(bytes, charset));
        buf.Release();
    }

    [Fact]
    public void TestSetCharSequenceWithTooLongSequence()
    {
        ByteBuf buffer = Unpooled.Buffer(4, 128);
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetCharSequence(0, sequence, Encoding.UTF8));
        buffer.Release();
    }

    [Fact]
    public void TestWriteCharSequence()
    {
        ByteBuf buffer = Unpooled.Buffer(4, 128);
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        int capacity = buffer.Capacity;
        // This should expand the buffer.
        buffer.WriteCharSequence(sequence, Encoding.UTF8);
        Assert.NotEqual(capacity, buffer.Capacity);
        buffer.Release();
    }

    [Fact]
    public void TestRetainedSliceIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, true, true);
        });
    }

    [Fact]
    public void TestRetainedSliceLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, true, false);
        });
    }

    [Fact]
    public void TestMixedSliceAIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, false, true);
        });
    }

    [Fact]
    public void TestMixedSliceALengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(true, false, false);
        });
    }

    [Fact]
    public void TestMixedSliceBIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, true, true);
        });
    }

    [Fact]
    public void TestMixedSliceBLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, true, false);
        });
    }

    [Fact]
    public void TestSliceIndexOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, false, true);
        });
    }

    [Fact]
    public void TestSliceLengthOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertSliceOutOfBounds(false, false, false);
        });
    }

    [Fact]
    public void TestReadSliceOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertReadSliceOutOfBounds(false);
        });
    }

    [Fact]
    public void TestReadRetainedSliceOutOfBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            AssertReadSliceOutOfBounds(true);
        });
    }

    [Fact]
    public void TestSliceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Slice();
        });
    }

    [Fact]
    public void TestSliceAfterRelease2()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Slice(0, 1);
        });
    }

    [Fact]
    public void TestRetainedSliceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedSlice();
        });
    }

    [Fact]
    public void TestRetainedSliceAfterRelease2()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedSlice(0, 1);
        });
    }

    [Fact]
    public void TestDuplicateAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Duplicate();
        });
    }

    [Fact]
    public void TestRetainedDuplicateAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().RetainedDuplicate();
        });
    }

    [Fact]
    public void TestSliceRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.RefCnt);
        Assert.True(buf.Slice().Release());
        Assert.Equal(0, buf.RefCnt);
    }

    [Fact]
    public void TestSliceIndex()
    {
        Assert.Equal(0, buffer.Slice(0, buffer.Capacity).ReaderIndex);
        Assert.Equal(0, buffer.Slice(0, buffer.Capacity - 1).ReaderIndex);
        Assert.Equal(0, buffer.Slice(1, buffer.Capacity - 1).ReaderIndex);
        Assert.Equal(0, buffer.Slice(1, buffer.Capacity - 2).ReaderIndex);

        Assert.Equal(buffer.Capacity, buffer.Slice(0, buffer.Capacity).WriterIndex);
        Assert.Equal(buffer.Capacity - 1, buffer.Slice(0, buffer.Capacity - 1).WriterIndex);
        Assert.Equal(buffer.Capacity - 1, buffer.Slice(1, buffer.Capacity - 1).WriterIndex);
        Assert.Equal(buffer.Capacity - 2, buffer.Slice(1, buffer.Capacity - 2).WriterIndex);
    }

    [Fact]
    public void TestRetainedSliceIndex()
    {
        ByteBuf retainedSlice = buffer.RetainedSlice(0, buffer.Capacity);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity - 1);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 1);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 2);
        Assert.Equal(0, retainedSlice.ReaderIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity);
        Assert.Equal(buffer.Capacity, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(0, buffer.Capacity - 1);
        Assert.Equal(buffer.Capacity - 1, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 1);
        Assert.Equal(buffer.Capacity - 1, retainedSlice.WriterIndex);
        retainedSlice.Release();

        retainedSlice = buffer.RetainedSlice(1, buffer.Capacity - 2);
        Assert.Equal(buffer.Capacity - 2, retainedSlice.WriterIndex);
        retainedSlice.Release();
    }

    [Fact]
    public void TestDuplicateOfSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf slice = buffer.Slice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();
    }

    [Fact]
    public void TestDuplicateOfRetainedSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf slice = buffer.RetainedSlice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();
    }

    [Fact]
    public void TestRetainedSliceOfNonRetainedDerivedBufferCoversTheReadableBytes()
    {
        for (int i = 0; i < buffer.Capacity; i++)
        {
            buffer.SetByte(i, i);
        }

        // a writer index below the capacity, and a reader index at zero or above
        for (int readerIndex = 0; readerIndex <= 1; readerIndex++)
        {
            buffer.SetIndex(readerIndex, buffer.Capacity - 2);
            ByteBuf retainedDuplicate = null;
            ByteBuf retainedSlice = null;

            try
            {
                retainedDuplicate = buffer.RetainedDuplicate();
                retainedSlice = buffer.RetainedSlice();

                foreach (ByteBuf derived in new ByteBuf[] { retainedDuplicate, retainedSlice })
                {
                    AssertRetainedSliceBeforeAndAfterRead(derived.Duplicate());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Slice());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Duplicate().Duplicate());
                    AssertRetainedSliceBeforeAndAfterRead(derived.Slice().Slice());
                }
            }
            finally
            {
                if (retainedDuplicate != null)
                {
                    retainedDuplicate.Release();
                }

                if (retainedSlice != null)
                {
                    retainedSlice.Release();
                }
            }

            Assert.Equal(1, buffer.RefCnt);
        }
    }

    [Fact]
    public void TestSequentialSlice1()
    {
        buffer.WriterIndex = 0;

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            byte[] value = new byte[BLOCK_SIZE];
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE];

        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            ByteBuf actualValue = buffer.ReadSlice(BLOCK_SIZE);
            // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
            Assert.Equal(WrappedBuffer(expectedValue), actualValue);

            // Make sure if it is a sliced buffer.
            actualValue.SetByte(0, unchecked((byte)(actualValue.GetByte(0) + 1)));
            Assert.Equal(buffer.GetByte(i), actualValue.GetByte(0));
        }
    }

    private void AssertSliceOutOfBounds(bool initRetainedSlice, bool finalRetainedSlice, bool indexOutOfBounds)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf slice = initRetainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 1, 2)
                                          : buf.Slice(buf.ReaderIndex + 1, 2);

        try
        {
            Assert.Equal(2, slice.Capacity);
            Assert.Equal(2, slice.MaxCapacity);
            int index = indexOutOfBounds ? 3 : 0;
            int length = indexOutOfBounds ? 0 : 3;

            if (finalRetainedSlice)
            {
                // This is expected to fail ... so no need to release.
                slice.RetainedSlice(index, length);
            }
            else
            {
                slice.Slice(index, length);
            }
        }
        finally
        {
            if (initRetainedSlice)
            {
                slice.Release();
            }

            buf.Release();
        }
    }

    private void AssertReadSliceOutOfBounds(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(100);

        try
        {
            buf.WriteZero(50);

            if (retainedSlice)
            {
                buf.ReadRetainedSlice(51);
            }
            else
            {
                buf.ReadSlice(51);
            }

            Assert.Fail();
        }
        finally
        {
            buf.Release();
        }
    }

    private static void AssertRetainedSliceBeforeAndAfterRead(ByteBuf buf)
    {
        AssertRetainedSliceMatchesTheReadableBytes(buf);
        buf.ReadByte();
        AssertRetainedSliceMatchesTheReadableBytes(buf);
    }

    private static void AssertRetainedSliceMatchesTheReadableBytes(ByteBuf buf)
    {
        ByteBuf retainedSlice = buf.RetainedSlice();

        try
        {
            Assert.Equal(buf.ReadableBytes, retainedSlice.ReadableBytes);
            Assert.Equal(buf.Slice(), retainedSlice);
        }
        finally
        {
            retainedSlice.Release();
        }
    }

    // CLR: ReadOnlyDirectByteBufferBufTest statically imports these AbstractByteBufTest helpers
    // (assertReadyOnlyNioBuffer*, testBytesInArrayMultipleThreads, testCopyMultipleThreads0,
    // testReadGatheringByteChannelMultipleThreads, testReadOutputStreamMultipleThreads and
    // testToStringMultipleThreads0). The single CLR bodies stay in AbstractByteBufTest; this nested type
    // gives them the original static-import names without duplicating them or clashing with test names.
    internal static class StaticImports
    {
        internal static void AssertReadyOnlyNioBuffer(ByteBuf buffer)
        {
            AbstractByteBufTest.AssertReadyOnlyNioBuffer(buffer);
        }

        internal static void AssertReadyOnlyNioBufferWithPositionLength(ByteBuf buffer)
        {
            AbstractByteBufTest.AssertReadyOnlyNioBufferWithPositionLength(buffer);
        }

        internal static void AssertReadyOnlyNioBuffers(ByteBuf buffer)
        {
            AbstractByteBufTest.AssertReadyOnlyNioBuffers(buffer);
        }

        internal static void AssertReadyOnlyNioBuffersWithPositionLength(ByteBuf buffer)
        {
            AbstractByteBufTest.AssertReadyOnlyNioBuffersWithPositionLength(buffer);
        }

        internal static Task TestBytesInArrayMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
        {
            return AssertBytesInArrayMultipleThreads(buffer, expectedBytes, slice);
        }

        internal static void TestCopyMultipleThreads0(ByteBuf buffer)
        {
            AssertCopyMultipleThreads(buffer);
        }

        internal static void TestReadGatheringByteChannelMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
        {
            AssertReadGatheringByteChannelMultipleThreads(buffer, expectedBytes, slice);
        }

        internal static void TestReadOutputStreamMultipleThreads(ByteBuf buffer, byte[] expectedBytes, bool slice)
        {
            AssertReadOutputStreamMultipleThreads(buffer, expectedBytes, slice);
        }

        internal static void TestToStringMultipleThreads0(ByteBuf buffer)
        {
            AssertToStringMultipleThreads(buffer);
        }
    }
}
