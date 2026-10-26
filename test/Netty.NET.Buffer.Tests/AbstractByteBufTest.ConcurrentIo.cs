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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestToStringMultipleThreads()
    {
        buffer.Clear();
        buffer.WriteBytes(Encoding.Latin1.GetBytes("Hello, World!"));
        AssertToStringMultipleThreads(buffer);
    }

    private static void AssertToStringMultipleThreads(ByteBuf buffer)
    {
        string expected = buffer.GetString(Encoding.Latin1);
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
                        Assert.Equal(expected, buffer.GetString(Encoding.Latin1));
                }
                catch (Exception cause) { errors.Enqueue(cause); }
            });
            threads.Add(thread);
        }
        foreach (Thread thread in threads) thread.Start();
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
                if (thread.IsAlive) thread.Interrupt();
        }
        finally
        {
            // Workers must relinquish borrowed storage before fixture cleanup releases its owner.
            foreach (Thread thread in threads) thread.Join();
        }
        if (!errors.IsEmpty) throw new AggregateException(errors);
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
                            try { Assert.Equal(expected, copy); }
                            finally { copy.Release(); }
                        }
                    }
                    catch (Exception cause) { errors.Enqueue(cause); }
                });
                threads.Add(thread);
            }
            foreach (Thread thread in threads) thread.Start();
            JoinAllAndReportErrors(threads, errors, TimeSpan.FromSeconds(60));
        }
        finally { expected.Release(); }
    }

    [Fact]
    public void TestDuplicateReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { AssertReadOutputStreamMultipleThreads(buffer, bytes, false); }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestSliceReadOutputStreamMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { AssertReadOutputStreamMultipleThreads(buffer, bytes, true); }
        finally { buffer.Release(); }
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
                        if (slice) buf = buffer.Slice();
                        else buf = buffer.Duplicate();
                        using var output = new MemoryStream();
                        while (buf.IsReadable)
                        {
                            // Never happens
                            // CLR: an unexpected IOException is reported by the outer worker catch.
                            buf.ReadBytes(output, buf.ReadableBytes);
                        }
                        Assert.Equal(expectedBytes, output.ToArray());
                        // CountDownLatch saturates at zero; at most nine already-started iterations may finish later.
                        if (Interlocked.Decrement(ref remaining) <= 0) completed.Set();
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
                    try { Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30))); }
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
            foreach (Thread thread in threads) thread.Join();
        }
        // Ensure failures in worker threads are propagated to the test thread.
        if (!errors.IsEmpty) throw new AggregateException("Worker thread failed", errors);
    }

    [Fact]
    public async Task TestDuplicateBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { await AssertBytesInArrayMultipleThreads(buffer, bytes, false); }
        finally { buffer.Release(); }
    }

    [Fact]
    public async Task TestSliceBytesInArrayMultipleThreads()
    {
        byte[] bytes = new byte[8];
        random.NextBytes(bytes);
        ByteBuf buffer = NewBuffer(8);
        buffer.WriteBytes(bytes);
        try { await AssertBytesInArrayMultipleThreads(buffer, bytes, true); }
        finally { buffer.Release(); }
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
                    if (slice) buf = buffer.Slice();
                    else buf = buffer.Duplicate();
                    byte[] array = new byte[8];
                    buf.ReadBytes(array);
                    Assert.Equal(expectedBytes, array);
                    Array.Fill(array, (byte)0);
                    buf.GetBytes(0, array);
                    Assert.Equal(expectedBytes, array);
                }
            }
            finally { endBarrier.SignalAndWait(); }
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
            foreach (Task task in tasks) await task.WaitAsync(TimeSpan.FromSeconds(1));
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
            finally { buffer.Release(); buffer2?.Release(); }
        }
        finally { File.Delete(file); }
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
            finally { buffer.Release(); buffer2?.Release(); }
        }
        finally { File.Delete(file); }
    }
}
