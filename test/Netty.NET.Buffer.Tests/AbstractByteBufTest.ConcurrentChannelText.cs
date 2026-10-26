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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestDuplicateReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { AssertReadGatheringByteChannelMultipleThreads(buffer, bytes, false); }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestSliceReadGatheringByteChannelMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { AssertReadGatheringByteChannelMultipleThreads(buffer, bytes, true); }
        finally { buffer.Release(); }
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
                        if (slice) buf = buffer.Slice();
                        else buf = buffer.Duplicate();
                        while (buf.IsReadable)
                        {
                            if (clock.Elapsed >= TimeSpan.FromSeconds(30))
                                throw new TimeoutException("The original concurrent channel timeout expired.");
                            // Never happens
                            // CLR: unexpected I/O errors reach the outer worker catch immediately.
                            buf.ReadBytes(channel.Socket, buf.ReadableBytes);
                        }
                        Assert.Equal(expectedBytes, channel.WrittenBytes(expectedBytes.Length));
                        if (Interlocked.Decrement(ref remaining) <= 0) completed.Set();
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
                    try { Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30))); }
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
            foreach (Thread thread in threads) thread.Join();
        }
        // Ensure failures in worker threads are propagated to the test thread.
        if (!errors.IsEmpty) throw new AggregateException("Worker thread failed", errors);
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
            catch { _peer?.Dispose(); Socket.Dispose(); throw; }
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

        public void Dispose() { _peer.Dispose(); Socket.Dispose(); }
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
        Exception failure = null;
        try
        {
            for (int i = 0; i < bufs.Length; i++)
            {
                bufs[i] = NewBuffer(bufSize);
                // Make the supplemental content assertion independent of stale pooled allocation contents.
                bufs[i].SetZero(0, bufSize);
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
                    for (int j = 0; j < iterations; j++) buf.SetString(0, text, encoding);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
            CancellationToken token = TestContext.Current.CancellationToken;
            for (int i = 0; i < bufs.Length; i++) await finish.WaitAsync(token);
            start.Release(bufs.Length);
            Task all = Task.WhenAll(tasks);
            try { await all.WaitAsync(token); }
            catch (OperationCanceledException)
            {
                // Propagate interrupted exceptions immediately.
                // CLR cancellation propagates after the finally block safely drains borrowed workers.
                throw;
            }
            catch { throw new AggregateException("Worker threads failed", all.Exception.InnerExceptions); }

            // Original success means all Futures completed. Also verify actual bytes and absolute indices.
            byte[] expected = new byte[cs.Length];
            Array.Fill(expected, (byte)'a');
            foreach (ByteBuf buf in bufs)
            {
                Assert.Equal(expected, ByteBufUtil.GetBytes(buf, 0, expected.Length));
                Assert.Equal(0, buf.ReaderIndex);
                Assert.Equal(0, buf.WriterIndex);
            }
        }
        catch (Exception cause) { failure = cause; throw; }
        finally
        {
            start.Release(bufs.Length);
            try { await Task.WhenAll(tasks); }
            catch when (failure != null) { }
            finally
            {
                foreach (ByteBuf buf in bufs) buf?.Release();
            }
        }
    }
}
