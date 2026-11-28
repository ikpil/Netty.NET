/*
 * Copyright 2012 The Netty Project
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 * https://www.apache.org/licenses/LICENSE-2.0
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Traffic;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// CLR: the testsuite's TrafficShapingHandlerTest (Netty.NET.Transport.Tests), not the handler module's class of
// the same name (Netty.NET.Handler.Tests.Traffic). The static @BeforeAll createGroup()/@AfterAll destroyGroup()
// run from a class fixture, as the GroupFixture of SocketEchoSourceTest. Each @Test is a [Fact] with the @Timeout
// of the source that runs the body through the harness (both allocators x the NIO socket combination; OIO is
// deprecated-not-ported). Promise<Boolean> from group.next().newPromise() is a TaskCompletionSource<bool> :
// setSuccess/setFailure are SetResult/SetException (which throw on a completed promise, as DefaultPromise does),
// await() is an await that suppresses the failure (ConfigureAwaitOptions.SuppressThrowing, as
// DatagramMulticastSourceTest), isSuccess() is IsCompletedSuccessfully and cause() the Task's exception. Java
// string concatenation prints numbers with the locale-independent Integer/Long/Float.toString and booleans as
// "true"/"false", so the messages use FormattableString.Invariant and Str(bool).
#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose promise wait has no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and sleeps do not observe the test cancellation.
public class TrafficShapingHandlerTest : AbstractSocketTest, IClassFixture<TrafficShapingHandlerTest.GroupFixture>
{
    // CLR: new hides the harness's instance logger, as the source's private static field hides the protected field of
    // AbstractComboTestsuiteTest.
    private static new readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(TrafficShapingHandlerTest));
    private static readonly IInternalLogger loggerServer = InternalLoggerFactory.GetInstance("ServerTSH");
    private static readonly IInternalLogger loggerClient = InternalLoggerFactory.GetInstance("ClientTSH");

    internal const int messageSize = 1024;
    internal const int bandwidthFactor = 12;
    internal const int minfactor = 3;
    internal const int maxfactor = bandwidthFactor + bandwidthFactor / 2;
    internal const long stepms = (1000 / bandwidthFactor - 10) / 10 * 10;
    internal static readonly long minimalms = Math.Max(stepms / 2, 20) / 10 * 10;
    internal const long check = 10;
    internal static readonly byte[] data = new byte[messageSize];

    private const string TRAFFIC = "traffic";
    private static string currentTestName;
    private static int currentTestRun;

    private static IEventExecutorGroup group;
    private static IEventExecutorGroup groupForGlobal;
    // CLR: Executors.newScheduledThreadPool(10) is a DefaultEventExecutorGroup(10), the executor family that schedules
    // (ScheduledExecutorService is IEventExecutorGroup, src/Netty.NET.Handler/Traffic/TrafficCounter.cs), as the handler
    // module's TrafficShapingHandlerTest maps newSingleThreadScheduledExecutor() to a DefaultEventExecutor;
    // schedule(runnable, delay, MILLISECONDS) is ScheduleAsync(action, TimeSpan.FromMilliseconds(delay)).
    private static readonly IEventExecutorGroup executor = new DefaultEventExecutorGroup(10);

    // CLR: ThreadLocalRandom.current() is Random.Shared.
    static TrafficShapingHandlerTest()
    {
        Random.Shared.NextBytes(data);
    }

    // CLR: JUnit runs the static @BeforeAll/@AfterAll methods once around all tests of the class. xUnit v3 creates a
    // class fixture "Just before the first test" and disposes it "once all the tests have finished"
    // (https://xunit.net/docs/shared-context), so this fixture calls createGroup() and destroyGroup() there.
    public sealed class GroupFixture : IDisposable
    {
        public GroupFixture()
        {
            CreateGroup();
        }

        public void Dispose()
        {
            DestroyGroup();
        }
    }

    // CLR: internal instead of the source's public, because xUnit1013 reports public non-test methods on a test class.
    internal static void CreateGroup()
    {
        logger.Info(FormattableString.Invariant($"Bandwidth: {minfactor} <= {bandwidthFactor} <= {maxfactor}") +
                    FormattableString.Invariant($" StepMs: {stepms} MinMs: {minimalms} CheckMs: {check}"));
        group = new DefaultEventExecutorGroup(8);
        groupForGlobal = new DefaultEventExecutorGroup(8);
    }

    // CLR: shutdownGracefully().sync() blocks until the group terminated; xUnit disposes the fixture synchronously.
    // ExecutorService.shutdown() starts the shutdown without waiting, as the discarded ShutdownGracefullyAsync() Task.
    internal static void DestroyGroup()
    {
        group.ShutdownGracefullyAsync().GetAwaiter().GetResult();
        groupForGlobal.ShutdownGracefullyAsync().GetAwaiter().GetResult();
        _ = executor.ShutdownGracefullyAsync();
    }

    private static long[] ComputeWaitRead(int[] multipleMessage)
    {
        long[] minimalWaitBetween = new long[multipleMessage.Length + 1];
        minimalWaitBetween[0] = 0;

        for (int i = 0; i < multipleMessage.Length; i++)
        {
            if (multipleMessage[i] > 1)
            {
                minimalWaitBetween[i + 1] = (multipleMessage[i] - 1) * stepms + minimalms;
            }
            else
            {
                minimalWaitBetween[i + 1] = 10;
            }
        }

        return minimalWaitBetween;
    }

    private static long[] ComputeWaitWrite(int[] multipleMessage)
    {
        long[] minimalWaitBetween = new long[multipleMessage.Length + 1];

        for (int i = 0; i < multipleMessage.Length; i++)
        {
            if (multipleMessage[i] > 1)
            {
                minimalWaitBetween[i] = (multipleMessage[i] - 1) * stepms + minimalms;
            }
            else
            {
                minimalWaitBetween[i] = 10;
            }
        }

        return minimalWaitBetween;
    }

    private static long[] ComputeWaitAutoRead(int []autoRead)
    {
        long [] minimalWaitBetween = new long[autoRead.Length + 1];
        minimalWaitBetween[0] = 0;

        for (int i = 0; i < autoRead.Length; i++)
        {
            if (autoRead[i] != 0)
            {
                if (autoRead[i] > 0)
                {
                    minimalWaitBetween[i + 1] = -1;
                }
                else
                {
                    minimalWaitBetween[i + 1] = check;
                }
            }
            else
            {
                minimalWaitBetween[i + 1] = 0;
            }
        }

        return minimalWaitBetween;
    }

    [Fact(Timeout = 10000)]
    public async Task TestNoTrafficShapping()
    {
        currentTestName = "TEST NO TRAFFIC";
        currentTestRun = 0;
        await Run(TestContext.Current, TestNoTrafficShapping0);
    }

    // CLR: xUnit1024 forbids overloading a test name, hence the 0 suffix on the source's public testXxx(sb, cb) bodies,
    // which are private here because xUnit1013 flags public non-test methods on a test class.
    private async Task TestNoTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 2, 1 };
        long[] minimalWaitBetween = null;
        await TestTrafficShapping0(sb, cb, false, false, false, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestWriteTrafficShapping()
    {
        currentTestName = "TEST WRITE";
        currentTestRun = 0;
        await Run(TestContext.Current, TestWriteTrafficShapping0);
    }

    private async Task TestWriteTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 2, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitWrite(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, false, true, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestReadTrafficShapping()
    {
        currentTestName = "TEST READ";
        currentTestRun = 0;
        await Run(TestContext.Current, TestReadTrafficShapping0);
    }

    private async Task TestReadTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 2, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitRead(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, true, false, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestWrite1TrafficShapping()
    {
        currentTestName = "TEST WRITE";
        currentTestRun = 0;
        await Run(TestContext.Current, TestWrite1TrafficShapping0);
    }

    private async Task TestWrite1TrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitWrite(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, false, true, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestRead1TrafficShapping()
    {
        currentTestName = "TEST READ";
        currentTestRun = 0;
        await Run(TestContext.Current, TestRead1TrafficShapping0);
    }

    private async Task TestRead1TrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitRead(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, true, false, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestWriteGlobalTrafficShapping()
    {
        currentTestName = "TEST GLOBAL WRITE";
        currentTestRun = 0;
        await Run(TestContext.Current, TestWriteGlobalTrafficShapping0);
    }

    private async Task TestWriteGlobalTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 2, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitWrite(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, false, true, true, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestReadGlobalTrafficShapping()
    {
        currentTestName = "TEST GLOBAL READ";
        currentTestRun = 0;
        await Run(TestContext.Current, TestReadGlobalTrafficShapping0);
    }

    private async Task TestReadGlobalTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = null;
        int[] multipleMessage = { 1, 2, 1, 1 };
        long[] minimalWaitBetween = ComputeWaitRead(multipleMessage);
        await TestTrafficShapping0(sb, cb, false, true, false, true, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestAutoReadTrafficShapping()
    {
        currentTestName = "TEST AUTO READ";
        currentTestRun = 0;
        await Run(TestContext.Current, TestAutoReadTrafficShapping0);
    }

    private async Task TestAutoReadTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = { 1, -1, -1, 1, -2, 0, 1, 0, -3, 0, 1, 2, 0 };
        int[] multipleMessage = new int[autoRead.Length];
        Array.Fill(multipleMessage, 1);
        long[] minimalWaitBetween = ComputeWaitAutoRead(autoRead);
        await TestTrafficShapping0(sb, cb, false, true, false, false, autoRead, minimalWaitBetween, multipleMessage);
    }

    [Fact(Timeout = 10000)]
    public async Task TestAutoReadGlobalTrafficShapping()
    {
        currentTestName = "TEST AUTO READ GLOBAL";
        currentTestRun = 0;
        await Run(TestContext.Current, TestAutoReadGlobalTrafficShapping0);
    }

    private async Task TestAutoReadGlobalTrafficShapping0(ServerBootstrap sb, Bootstrap cb)
    {
        int[] autoRead = { 1, -1, -1, 1, -2, 0, 1, 0, -3, 0, 1, 2, 0 };
        int[] multipleMessage = new int[autoRead.Length];
        Array.Fill(multipleMessage, 1);
        long[] minimalWaitBetween = ComputeWaitAutoRead(autoRead);
        await TestTrafficShapping0(sb, cb, false, true, false, true, autoRead, minimalWaitBetween, multipleMessage);
    }

    /**
     *
     * @param additionalExecutor
     *            shall the pipeline add the handler using an additional executor
     * @param limitRead
     *            True to set Read Limit on Server side
     * @param limitWrite
     *            True to set Write Limit on Client side
     * @param globalLimit
     *            True to change Channel to Global TrafficShapping
     * @param minimalWaitBetween
     *            time in ms that should be waited before getting the final result (note: for READ the values are
     *            right shifted once, the first value being 0)
     * @param multipleMessage
     *            how many message to send at each step (for READ: the first should be 1, as the two last steps to
     *            ensure correct testing)
     * @throws Throwable
     */
    private static async Task TestTrafficShapping0(
            ServerBootstrap sb, Bootstrap cb, bool additionalExecutor,
            bool limitRead, bool limitWrite, bool globalLimit, int[] autoRead,
            long[] minimalWaitBetween, int[] multipleMessage)
    {

        currentTestRun++;
        logger.Info("TEST: " + currentTestName + FormattableString.Invariant($" RUN: {currentTestRun}") +
                    " Exec: " + Str(additionalExecutor) + " Read: " + Str(limitRead) + " Write: " + Str(limitWrite) +
                    " Global: " + Str(globalLimit));
        ServerHandler sh = new ServerHandler(autoRead, multipleMessage);
        // CLR: group.next().newPromise() is a TaskCompletionSource<bool> whose continuations do not run inside the
        // completing handler call (see the class comment).
        TaskCompletionSource<bool> promise = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ClientHandler ch = new ClientHandler(promise, minimalWaitBetween, multipleMessage,
                                             autoRead);

        AbstractTrafficShapingHandler handler;

        if (limitRead)
        {
            if (globalLimit)
            {
                handler = new GlobalTrafficShapingHandler(groupForGlobal, 0, bandwidthFactor * messageSize, check);
            }
            else
            {
                handler = new ChannelTrafficShapingHandler(0, bandwidthFactor * messageSize, check);
            }
        }
        else if (limitWrite)
        {
            if (globalLimit)
            {
                handler = new GlobalTrafficShapingHandler(groupForGlobal, bandwidthFactor * messageSize, 0, check);
            }
            else
            {
                handler = new ChannelTrafficShapingHandler(bandwidthFactor * messageSize, 0, check);
            }
        }
        else
        {
            handler = null;
        }

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in SocketEchoSourceTest.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(c =>
        {
            if (limitRead)
            {
                c.Pipeline.AddLast(TRAFFIC, handler);
            }

            c.Pipeline.AddLast(sh);
        }));
        cb.Handler(new ActionChannelInitializer<IChannel>(c =>
        {
            if (limitWrite)
            {
                c.Pipeline.AddLast(TRAFFIC, handler);
            }

            c.Pipeline.AddLast(ch);
        }));

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        int totalNb = 0;

        for (int i = 1; i < multipleMessage.Length; i++)
        {
            totalNb += multipleMessage[i];
        }

        // CLR: the boxed Long start/stop of the source are long.
        long start = TrafficCounter.MilliSecondFromNano();
        int nb = multipleMessage[0];

        for (int i = 0; i < nb; i++)
        {
            _ = cc.WriteAsync(cc.Alloc().Buffer().WriteBytes(data));
        }

        cc.Flush();

        await ((Task) promise.Task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        long stop = TrafficCounter.MilliSecondFromNano();
        Assert.True(promise.Task.IsCompletedSuccessfully, "Error during execution of TrafficShapping: " + Cause(promise.Task));

        float average = (totalNb * messageSize) / (float) (stop - start);
        logger.Info("TEST: " + currentTestName + FormattableString.Invariant($" RUN: {currentTestRun}") +
                    FormattableString.Invariant($" Average of traffic: {average} compare to {bandwidthFactor}"));
        await sh.channel.CloseAsync();
        await ch.channel.CloseAsync();
        await sc.CloseAsync();

        if (autoRead != null)
        {
            // for extra release call in AutoRead
            // CLR: Thread.sleep(minimalms) is an awaited Task.Delay, as in SocketEchoSourceTest.
            await Task.Delay(TimeSpan.FromMilliseconds(minimalms));
        }

        if (autoRead == null && minimalWaitBetween != null)
        {
            Assert.True(average <= maxfactor,
                FormattableString.Invariant($"Overall Traffic not ok since > {maxfactor}: {average}"));

            if (additionalExecutor)
            {
                // Oio is not as good when using additionalExecutor
                Assert.True(average >= 0.25, FormattableString.Invariant($"Overall Traffic not ok since < 0.25: {average}"));
            }
            else
            {
                Assert.True(average >= minfactor,
                    FormattableString.Invariant($"Overall Traffic not ok since < {minfactor}: {average}"));
            }
        }

        if (handler != null && globalLimit)
        {
            ((GlobalTrafficShapingHandler) handler).Release();
        }

        if (sh.exception.Get() != null && !(sh.exception.Get() is IOException))
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null && !(ch.exception.Get() is IOException))
        {
            throw ch.exception.Get();
        }
        if (sh.exception.Get() != null)
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null)
        {
            throw ch.exception.Get();
        }
    }

    // CLR: Promise.cause() is null unless the promise failed; String.valueOf(null) is "null".
    private static string Cause(Task task)
    {
        Exception cause = task.IsFaulted ? task.Exception.InnerException : null;
        return cause != null ? cause.ToString() : "null";
    }

    // CLR: a boolean concatenated into a Java string is "true"/"false".
    private static string Str(bool value)
    {
        return value ? "true" : "false";
    }

    // CLR: Throwable.printStackTrace() prints to the standard error stream, as in SocketEchoSourceTest.
    private static void PrintStackTrace(Exception cause)
    {
        Console.Error.WriteLine(cause);
    }

    private class ClientHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        internal volatile int step;
        // first message will always be validated
        private long currentLastTime = TrafficCounter.MilliSecondFromNano();
        private readonly long[] minimalWaitBetween;
        private readonly int[] multipleMessage;
        private readonly int[] autoRead;
        internal readonly TaskCompletionSource<bool> promise;

        internal ClientHandler(TaskCompletionSource<bool> promise, long[] minimalWaitBetween, int[] multipleMessage,
                               int[] autoRead)
        {
            this.minimalWaitBetween = minimalWaitBetween;
            this.multipleMessage = (int[]) multipleMessage.Clone();
            this.promise = promise;
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            long lastTimestamp = 0;
            loggerClient.Debug(FormattableString.Invariant($"Step: {step} Read: {@in.ReadableBytes / 8} blocks"));

            while (@in.IsReadable())
            {
                lastTimestamp = @in.ReadLong();
                multipleMessage[step]--;
            }

            if (multipleMessage[step] > 0)
            {
                // still some message to get
                return;
            }

            long minimalWait = minimalWaitBetween != null? minimalWaitBetween[step] : 0;
            int ar = 0;

            if (autoRead != null)
            {
                if (step > 0 && autoRead[step - 1] != 0)
                {
                    ar = autoRead[step - 1];
                }
            }

            loggerClient.Info(FormattableString.Invariant($"Step: {step} Interval: {lastTimestamp - currentLastTime}") +
                              FormattableString.Invariant($" compareTo {minimalWait} ({ar})"));
            Assert.True(lastTimestamp - currentLastTime >= minimalWait,
                    FormattableString.Invariant(
                            $"The interval of time is incorrect:{lastTimestamp - currentLastTime} not> {minimalWait}"));
            currentLastTime = lastTimestamp;
            step++;

            if (multipleMessage.Length > step)
            {
                int nb = multipleMessage[step];

                for (int i = 0; i < nb; i++)
                {
                    _ = channel.WriteAsync(channel.Alloc().Buffer().WriteBytes(data));
                }

                channel.Flush();
            }
            else
            {
                promise.SetResult(true);
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (exception.CompareAndSet(null, cause))
            {
                PrintStackTrace(cause);
                promise.SetException(cause);
                _ = ctx.CloseAsync();
            }
        }
    }

    private class ServerHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly int[] autoRead;
        private readonly int[] multipleMessage;
        internal volatile IChannel channel;
        internal volatile int step;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        internal ServerHandler(int[] autoRead, int[] multipleMessage)
        {
            this.autoRead = autoRead;
            this.multipleMessage = (int[]) multipleMessage.Clone();
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            int nb = actual.Length / messageSize;
            loggerServer.Info(FormattableString.Invariant($"Step: {step} Read: {nb} blocks"));
            @in.ReadBytes(actual);
            long timestamp = TrafficCounter.MilliSecondFromNano();
            int isAutoRead = 0;
            int laststep = step;

            for (int i = 0; i < nb; i++)
            {
                multipleMessage[step]--;

                if (multipleMessage[step] == 0)
                {
                    // setAutoRead test
                    if (autoRead != null)
                    {
                        isAutoRead = autoRead[step];
                    }

                    step++;
                }
            }

            if (laststep != step)
            {
                // setAutoRead test
                if (autoRead != null && isAutoRead != 2)
                {
                    if (isAutoRead != 0)
                    {
                        loggerServer.Info(FormattableString.Invariant($"Step: {step} Set AutoRead: ") +
                                          Str(isAutoRead > 0));
                        channel.Config.SetAutoRead(isAutoRead > 0);
                    }
                    else
                    {
                        loggerServer.Info(FormattableString.Invariant($"Step: {step} AutoRead: NO"));
                    }
                }
            }

            // CLR: Thread.sleep(10) blocks the handler's thread for 10 ms, as Thread.Sleep does.
            Thread.Sleep(10);
            loggerServer.Debug(FormattableString.Invariant($"Step: {step} Write: {nb}"));

            for (int i = 0; i < nb; i++)
            {
                _ = channel.WriteAsync(Unpooled.CopyLong(timestamp));
            }

            channel.Flush();

            if (laststep != step)
            {
                // setAutoRead test
                if (isAutoRead != 0)
                {
                    if (isAutoRead < 0)
                    {
                        int exactStep = step;
                        long wait = isAutoRead == -1? minimalms : stepms + minimalms;

                        if (isAutoRead == -3)
                        {
                            wait = stepms * 3;
                        }

                        _ = executor.ScheduleAsync(() =>
                        {
                            loggerServer.Info(FormattableString.Invariant($"Step: {exactStep} Reset AutoRead"));
                            channel.Config.SetAutoRead(true);
                        }, TimeSpan.FromMilliseconds(wait));
                    }
                    else
                    {
                        if (isAutoRead > 1)
                        {
                            loggerServer.Debug(FormattableString.Invariant($"Step: {step} Will Set AutoRead: True"));
                            int exactStep = step;
                            _ = executor.ScheduleAsync(() =>
                            {
                                loggerServer.Info(FormattableString.Invariant($"Step: {exactStep} Set AutoRead: True"));
                                channel.Config.SetAutoRead(true);
                            }, TimeSpan.FromMilliseconds(stepms + minimalms));
                        }
                    }
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (exception.CompareAndSet(null, cause))
            {
                PrintStackTrace(cause);
                _ = ctx.CloseAsync();
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
