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
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Local;

public sealed class LocalIoHandler : IIoHandler
{
    private readonly HashSet<ILocalIoHandle> _registeredChannels = new HashSet<ILocalIoHandle>(64, ReferenceEqualityComparer.Instance);
    private readonly IThreadAwareExecutor _executor;
    private volatile Thread _executionThread;
    // CLR: java.util.concurrent.locks.LockSupport keeps one permit per thread. The handler is only run by its
    // executor thread, so one Monitor-guarded permit gives parkNanos/unpark the same semantics.
    // https://learn.microsoft.com/dotnet/api/system.threading.monitor.wait
    private readonly object _parkLock = new object();
    private bool _permit;

    private LocalIoHandler(IThreadAwareExecutor executor)
    {
        _executor = ObjectUtil.CheckNotNull(executor, "executor");
    }

    /**
     * Returns a new {@link IoHandlerFactory} that creates {@link LocalIoHandler} instances.
     */
    public static IIoHandlerFactory NewFactory()
    {
        return Factory.INSTANCE;
    }

    // CLR: the LocalIoHandler::new method reference becomes a factory class.
    private sealed class Factory : IIoHandlerFactory
    {
        internal static readonly Factory INSTANCE = new Factory();

        public IIoHandler NewHandler(IThreadAwareExecutor ioExecutor) => new LocalIoHandler(ioExecutor);
    }

    // CLR: ArgumentException replaces IllegalArgumentException.
    private static ILocalIoHandle Cast(IIoHandle handle)
    {
        if (handle is ILocalIoHandle localIoHandle)
        {
            return localIoHandle;
        }
        throw new ArgumentException("IoHandle of type " + StringUtil.SimpleClassName(handle) + " not supported");
    }

    public int Run(IIoHandlerContext context)
    {
        if (_executionThread == null)
        {
            _executionThread = Thread.CurrentThread;
        }
        if (context.CanBlock())
        {
            // Just block until there is a task ready to process or wakeup(...) is called.
            ParkNanos(context.DelayNanos(SystemTimer.NanoTime()));
        }

        if (context.ShouldReportActiveIoTime())
        {
            context.ReportActiveIoTime(0);
        }
        return 0;
    }

    public void Wakeup()
    {
        if (!_executor.IsExecutorThread(Thread.CurrentThread))
        {
            Thread thread = _executionThread;
            if (thread != null)
            {
                // Wakeup if we block at the moment.
                Unpark();
            }
        }
    }

    public void PrepareToDestroy()
    {
        foreach (ILocalIoHandle handle in _registeredChannels)
        {
            handle.CloseNow();
        }
        _registeredChannels.Clear();
    }

    public void Destroy()
    {
    }

    // CLR: InvalidOperationException replaces IllegalStateException.
    public IIoRegistration Register(IIoHandle handle)
    {
        ILocalIoHandle localHandle = Cast(handle);
        if (_registeredChannels.Add(localHandle))
        {
            LocalIoRegistration registration = new LocalIoRegistration(this, _executor, localHandle);
            localHandle.Registered();
            return registration;
        }
        throw new InvalidOperationException();
    }

    public bool IsCompatible(Type handleType)
    {
        return typeof(ILocalIoHandle).IsAssignableFrom(handleType);
    }

    private void ParkNanos(long nanos)
    {
        lock (_parkLock)
        {
            if (!_permit && nanos > 0)
            {
                long millis = nanos >= long.MaxValue - 999_999 ? long.MaxValue : (nanos + 999_999) / 1_000_000;
                Monitor.Wait(_parkLock, millis > int.MaxValue ? Timeout.Infinite : (int)millis);
            }
            _permit = false;
        }
    }

    private void Unpark()
    {
        lock (_parkLock)
        {
            _permit = true;
            Monitor.Pulse(_parkLock);
        }
    }

    private sealed class LocalIoRegistration : IIoRegistration
    {
        private readonly AtomicBoolean _canceled = new AtomicBoolean();
        private readonly LocalIoHandler _handler;
        private readonly IThreadAwareExecutor _executor;
        private readonly ILocalIoHandle _handle;

        internal LocalIoRegistration(LocalIoHandler handler, IThreadAwareExecutor executor, ILocalIoHandle handle)
        {
            _handler = handler;
            _executor = executor;
            _handle = handle;
        }

        public T Attachment<T>()
        {
            return default;
        }

        // CLR: NotSupportedException replaces UnsupportedOperationException.
        public long Submit(IIoOps ops)
        {
            throw new NotSupportedException();
        }

        public bool IsValid()
        {
            return !_canceled.Get();
        }

        public bool Cancel()
        {
            if (!_canceled.CompareAndSet(false, true))
            {
                return false;
            }
            if (_executor.IsExecutorThread(Thread.CurrentThread))
            {
                Cancel0();
            }
            else
            {
                _executor.Execute(Cancel0);
            }
            return true;
        }

        private void Cancel0()
        {
            if (_handler._registeredChannels.Remove(_handle))
            {
                _handle.Unregistered();
            }
        }
    }
}
