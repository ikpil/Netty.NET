/*
 * Copyright 2024 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent teardown.
public class SingleThreadIoEventLoopSourceTest
{
    [Fact]
    public void TestDefaultIoRatio()
    {
        SingleThreadIoEventLoop loop = new SingleThreadIoEventLoop(null,
                new BackgroundThreadFactory(Executors.DefaultThreadFactory()),
                new TestIoHandlerFactory(e => new TestIoHandler(e)));
        Assert.Equal(SingleThreadIoEventLoop.DEFAULT_IO_RATIO, loop.GetIoRatio());
        loop.ShutdownGracefullyAsync();
    }

    [Fact]
    public void TestSetIoRatio()
    {
        SingleThreadIoEventLoop loop = new SingleThreadIoEventLoop(null,
                new BackgroundThreadFactory(Executors.DefaultThreadFactory()),
                new TestIoHandlerFactory(e => new TestIoHandler(e)));
        loop.SetIoRatio(50);
        Assert.Equal(50, loop.GetIoRatio());
        // CLR: ArgumentException replaces IllegalArgumentException (ObjectUtil.checkInRange).
        Assert.Throws<ArgumentException>(() => loop.SetIoRatio(0));
        Assert.Throws<ArgumentException>(() => loop.SetIoRatio(101));
        loop.ShutdownGracefullyAsync();
    }

    // CLR: the local class TestIoHandler2 of the Java method is a nested class.
    private class TestIoHandler2 : TestIoHandler
    {
        internal TestIoHandler2(IThreadAwareExecutor executor) : base(executor)
        {
        }
    }

    [Fact]
    public void TestIsIoType()
    {
        IIoEventLoopGroup group = new SingleThreadIoEventLoop(null,
                new BackgroundThreadFactory(Executors.DefaultThreadFactory()),
                new TestIoHandlerFactory(e => new TestIoHandler(e)));
        Assert.True(group.IsIoType(typeof(TestIoHandler)));
        Assert.False(group.IsIoType(typeof(TestIoHandler2)));
        group.ShutdownGracefullyAsync();
    }

    private sealed class CompatibleTestIoHandler : TestIoHandler
    {
        internal CompatibleTestIoHandler(IThreadAwareExecutor executor) : base(executor)
        {
        }

        public override bool IsCompatible(Type handleType)
        {
            return handleType == typeof(TestIoHandle);
        }
    }

    // CLR: the anonymous TestIoHandle subclass of the Java method is a nested class.
    private sealed class AnonymousTestIoHandle : TestIoHandle { }

    [Fact]
    public void TestIsCompatible()
    {
        IIoHandle handle = new AnonymousTestIoHandle();
        IIoEventLoopGroup group = new SingleThreadIoEventLoop(null,
                new BackgroundThreadFactory(Executors.DefaultThreadFactory()),
                new TestIoHandlerFactory(e => new CompatibleTestIoHandler(e)));
        Assert.True(group.IsCompatible(typeof(TestIoHandle)));
        Assert.False(group.IsCompatible(handle.GetType()));
        group.ShutdownGracefullyAsync();
    }

    // CLR: TestIoHandler.run blocks on its semaphore until wakeup(); shutdownGracefully() releases one permit,
    // and the quiet-period pass of confirmShutdown() only queues WAKEUP_TASK, so the next run() never returns and
    // the loop thread outlives the test, in Java as well. The forked JVM of the Java build exits regardless; the
    // xUnit v3 runner waits for foreground threads and then reports a fatal error, so the loop threads of these
    // tests are background threads.
    // https://learn.microsoft.com/dotnet/api/system.threading.thread.isbackground
    private sealed class BackgroundThreadFactory(IThreadFactory threadFactory) : IThreadFactory
    {
        public Thread NewThread(Action r)
        {
            Thread thread = threadFactory.NewThread(r);
            thread.IsBackground = true;
            return thread;
        }
    }

    private sealed class TestThreadFactory : IThreadFactory
    {
        internal readonly BlockingCollection<Thread> Threads = new();

        public Thread NewThread(Action r)
        {
            // CLR: a background thread for the reason given on BackgroundThreadFactory.
            Thread thread = new Thread(() => r()) { IsBackground = true };
            Threads.Add(thread);
            return thread;
        }
    }

    private sealed class AlwaysCompatibleTestIoHandler : TestIoHandler
    {
        internal AlwaysCompatibleTestIoHandler(IThreadAwareExecutor executor) : base(executor)
        {
        }

        public override bool IsCompatible(Type handleType)
        {
            return true;
        }
    }

    [Fact(Skip = "This test is flaky and will often stall the build")]
    public async Task TestSuspendingWhileRegistrationActive()
    {
        TestThreadFactory threadFactory = new TestThreadFactory();
        IIoEventLoop loop = new SingleThreadIoEventLoop(null, threadFactory,
                new TestIoHandlerFactory(eventLoop => new AlwaysCompatibleTestIoHandler(eventLoop)));
        Assert.False(loop.IsSuspended());
        IIoRegistration registration = await loop.RegisterAsync(new TestIoHandle());
        Thread currentThread = threadFactory.Threads.Take();
        Assert.True(currentThread.IsAlive);
        Assert.True(loop.TrySuspend());

        // Still should be alive as until the registration is cancelled we can not suspend the loop.
        Assert.True(currentThread.IsAlive);

        registration.Cancel();
        Assert.False(registration.IsValid());

        // The current thread should be able to die now.
        currentThread.Join();

        Assert.Equal(0, threadFactory.Threads.Count);
        await loop.ShutdownGracefullyAsync();
    }

    // CLR: the TestIoHandler::new method references become a factory delegate.
    private sealed class TestIoHandlerFactory(Func<IThreadAwareExecutor, IIoHandler> newHandler) : IIoHandlerFactory
    {
        public IIoHandler NewHandler(IThreadAwareExecutor ioExecutor)
        {
            return newHandler(ioExecutor);
        }
    }

    private class TestIoHandler : IIoHandler
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);
        private readonly IThreadAwareExecutor _executor;

        internal TestIoHandler(IThreadAwareExecutor executor)
        {
            _executor = executor;
        }

        public void PrepareToDestroy()
        {
            // NOOP
        }

        public void Destroy()
        {
            // NOOP
        }

        public IIoRegistration Register(IIoHandle handle)
        {
            return new TestIoRegistration();
        }

        // CLR: the anonymous IoRegistration of register(IoHandle) becomes a nested class.
        private sealed class TestIoRegistration : IIoRegistration
        {
            private readonly AtomicBoolean _canceled = new AtomicBoolean();

            public T Attachment<T>()
            {
                return default;
            }

            public long Submit(IIoOps ops)
            {
                return 0;
            }

            public bool Cancel()
            {
                return _canceled.CompareAndSet(false, true);
            }

            public bool IsValid()
            {
                return !_canceled.Get();
            }
        }

        public void Wakeup()
        {
            _semaphore.Release();
        }

        public int Run(IIoHandlerContext context)
        {
            _semaphore.Wait();
            return 0;
        }

        public virtual bool IsCompatible(Type handleType)
        {
            return false;
        }
    }

    private class TestIoHandle : IIoHandle
    {
        public void Handle(IIoRegistration registration, IIoEvent readyOps)
        {
            // NOOP
        }

        public void Close()
        {
            // NOOP
        }
    }
}
