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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels.Nio;

/**
 * {@link IoHandler} implementation which register the {@link IoHandle}'s to a {@link Selector}.
 */
// CLR: java.nio.channels.Selector/SelectionKey/SelectorProvider are jvm-only-replaced. The private SocketSelector
// below plays the Selector role with Socket.Select over the registered sockets and a self-connected loopback UDP
// socket as its wakeup pipe, the design of OpenJDK's select()-based WindowsSelectorImpl (fd sets plus a wakeup
// socket pair). Readiness is reported exactly as SelectionKey ready ops: OP_READ/OP_ACCEPT from the read set,
// OP_WRITE/OP_CONNECT from the write set and a failed non-blocking connect from the error set.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.select
// https://learn.microsoft.com/windows/win32/api/winsock2/nf-winsock2-select
// https://github.com/openjdk/jdk/blob/master/src/java.base/windows/classes/sun/nio/ch/WindowsSelectorImpl.java
public sealed class NioIoHandler : IIoHandler
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(NioIoHandler));

    private const int CLEANUP_INTERVAL = 256; // XXX Hard-coded value, but won't need customization.

    private const int MIN_PREMATURE_SELECTOR_RETURNS = 3;
    private static readonly int SELECTOR_AUTO_REBUILD_THRESHOLD;

    private readonly Func<int> _selectNowSupplier;

    // Workaround for JDK NIO bug.
    //
    // See:
    // - https://bugs.openjdk.java.net/browse/JDK-6427854 for first few dev (unreleased) builds of JDK 7
    // - https://bugs.openjdk.java.net/browse/JDK-6527572 for JDK prior to 5.0u15-rev and 6u10
    // - https://github.com/netty/netty/issues/203
    // CLR: io.netty.noKeySetOptimization only selects the JDK SelectedSelectionKeySet reflection hack, which has no
    // counterpart; the selected set below is already an array-backed list.
    static NioIoHandler()
    {
        int selectorAutoRebuildThreshold = SystemPropertyUtil.GetInt("io.netty.selectorAutoRebuildThreshold", 512);
        if (selectorAutoRebuildThreshold < MIN_PREMATURE_SELECTOR_RETURNS)
        {
            selectorAutoRebuildThreshold = 0;
        }

        SELECTOR_AUTO_REBUILD_THRESHOLD = selectorAutoRebuildThreshold;

        if (logger.IsDebugEnabled())
        {
            logger.Debug("-Dio.netty.selectorAutoRebuildThreshold: {}", SELECTOR_AUTO_REBUILD_THRESHOLD);
        }
    }

    /**
     * The NIO {@link Selector}.
     */
    private SocketSelector _selector;

    /**
     * Boolean that controls determines if a blocked Selector.select should
     * break out of its selection process. In our case we use a timeout for
     * the select method and the select method will block for that time unless
     * waken up.
     */
    private readonly AtomicBoolean _wakenUp = new AtomicBoolean();

    private readonly ISelectStrategy _selectStrategy;
    private readonly IThreadAwareExecutor _executor;
    private int _cancelledKeys;
    private bool _needsToSelectAgain;

    private NioIoHandler(IThreadAwareExecutor executor, ISelectStrategy strategy)
    {
        _executor = ObjectUtil.CheckNotNull(executor, "executionContext");
        _selectStrategy = ObjectUtil.CheckNotNull(strategy, "selectStrategy");
        _selectNowSupplier = SelectNow;
        _selector = OpenSelector();
    }

    // CLR: ChannelException wraps the SocketException of the wakeup socket as it wraps the IOException of
    // SelectorProvider.openSelector().
    private static SocketSelector OpenSelector()
    {
        try
        {
            return new SocketSelector();
        }
        catch (SocketException e)
        {
            throw new ChannelException("failed to open a new selector", e);
        }
    }

    // CLR: a cancelled registration leaves the selector's key set at once, so no cancelled keys are subtracted.
    internal int NumRegistered()
    {
        return _selector.Keys().Count;
    }

    internal ICollection<DefaultNioRegistration> RegisteredSet()
    {
        return _selector.Keys();
    }

    internal void RebuildSelector0()
    {
        SocketSelector oldSelector = _selector;
        SocketSelector newSelector;

        if (oldSelector == null)
        {
            return;
        }

        try
        {
            newSelector = OpenSelector();
        }
        catch (Exception e)
        {
            logger.Warn("Failed to create a new Selector.", e);
            return;
        }

        // Register all channels to the new Selector.
        int nChannels = 0;
        foreach (DefaultNioRegistration handle in new List<DefaultNioRegistration>(oldSelector.Keys()))
        {
            try
            {
                if (!handle.IsValid() || newSelector.KeyFor(handle.Handle().SelectableChannel()) != null)
                {
                    continue;
                }

                handle.Register(newSelector);
                nChannels++;
            }
            catch (Exception e)
            {
                logger.Warn("Failed to re-register a NioHandle to the new Selector.", e);
                handle.Cancel();
            }
        }

        _selector = newSelector;

        try
        {
            // time to close the old selector as everything else is registered to the new one
            oldSelector.Close();
        }
        catch (Exception t)
        {
            if (logger.IsWarnEnabled())
            {
                logger.Warn("Failed to close the old Selector.", t);
            }
        }

        if (logger.IsInfoEnabled())
        {
            logger.Info("Migrated " + nChannels + " channel(s) to the new Selector.");
        }
    }

    // CLR: ArgumentException replaces IllegalArgumentException.
    private static INioIoHandle NioHandle(IIoHandle handle)
    {
        if (handle is INioIoHandle nioIoHandle)
        {
            return nioIoHandle;
        }
        throw new ArgumentException("IoHandle of type " + StringUtil.SimpleClassName(handle) + " not supported");
    }

    private static NioIoOps Cast(IIoOps ops)
    {
        if (ops is NioIoOps nioIoOps)
        {
            return nioIoOps;
        }
        throw new ArgumentException("IoOps of type " + StringUtil.SimpleClassName(ops) + " not supported");
    }

    // CLR: the SelectionKey attachment is the registration's current interest set as NioIoOps, the key state
    // (interestOps()) that the transport reads from it.
    internal sealed class DefaultNioRegistration : IIoRegistration
    {
        private readonly AtomicBoolean _canceled = new AtomicBoolean();
        private readonly NioIoHandler _ioHandler;
        private readonly INioIoHandle _handle;
        private volatile SocketSelector _key;
        private volatile int _interestOps;
        internal int ReadyOps;
        internal int SelectedRound;

        internal DefaultNioRegistration(NioIoHandler ioHandler, IThreadAwareExecutor executor, INioIoHandle handle,
            NioIoOps initialOps, SocketSelector selector)
        {
            _ioHandler = ioHandler;
            _handle = handle;
            _interestOps = initialOps._value;
            selector.Register(handle.SelectableChannel(), this);
            _key = selector;
        }

        internal INioIoHandle Handle()
        {
            return _handle;
        }

        internal int InterestOps => _interestOps;

        internal void Register(SocketSelector selector)
        {
            selector.Register(_handle.SelectableChannel(), this);
            _key.Cancel(_handle.SelectableChannel(), this);
            _key = selector;
        }

        public T Attachment<T>()
        {
            return (T)(object)NioIoOps.ValueOf(_interestOps);
        }

        // CLR: a JDK key becomes invalid when it is cancelled or its channel is closed.
        public bool IsValid()
        {
            return !_canceled.Get() && SocketSelector.IsOpen(_handle.SelectableChannel());
        }

        public long Submit(IIoOps ops)
        {
            if (!IsValid())
            {
                return -1;
            }
            int v = Cast(ops)._value;
            _interestOps = v;
            return v;
        }

        public bool Cancel()
        {
            if (!_canceled.CompareAndSet(false, true))
            {
                return false;
            }
            _key.Cancel(_handle.SelectableChannel(), this);
            _ioHandler._cancelledKeys++;
            if (_ioHandler._cancelledKeys >= CLEANUP_INTERVAL)
            {
                _ioHandler._cancelledKeys = 0;
                _ioHandler._needsToSelectAgain = true;
            }
            _handle.Unregistered();
            return true;
        }

        internal void Close()
        {
            Cancel();
            try
            {
                _handle.Close();
            }
            catch (Exception e)
            {
                logger.Debug("Exception during closing " + _handle, e);
            }
        }

        internal void Handle(int ready)
        {
            if (!IsValid())
            {
                return;
            }
            _handle.Handle(this, NioIoOps.EventOf(ready));
        }

        internal bool IsCanceled() => _canceled.Get();
    }

    // CLR: InvalidOperationException stands for java.nio.channels.IllegalBlockingModeException, which register()
    // throws for a channel that is still in blocking mode.
    public IIoRegistration Register(IIoHandle handle)
    {
        INioIoHandle nioHandle = NioHandle(handle);
        NioIoOps ops = NioIoOps.NONE;
        if (nioHandle.SelectableChannel().Blocking)
        {
            throw new InvalidOperationException("IllegalBlockingModeException: the socket must be non-blocking.");
        }
        IIoRegistration registration = new DefaultNioRegistration(this, _executor, nioHandle, ops, _selector);
        handle.Registered();
        return registration;
    }

    public int Run(IIoHandlerContext context)
    {
        int handled = 0;
        try
        {
            try
            {
                switch (_selectStrategy.CalculateStrategy(_selectNowSupplier, !context.CanBlock()))
                {
                    case ISelectStrategy.CONTINUE:
                        if (context.ShouldReportActiveIoTime())
                        {
                            context.ReportActiveIoTime(0); // Report zero as we did no I/O.
                        }
                        return 0;

                    case ISelectStrategy.BUSY_WAIT:
                    // fall-through to SELECT since the busy-wait is not supported with NIO

                    case ISelectStrategy.SELECT:
                        // CLR: Common's AtomicBoolean.Set(bool) returns the previous value, as getAndSet(boolean).
                        Select(context, _wakenUp.Set(false));

                        // 'wakenUp.compareAndSet(false, true)' is always evaluated
                        // before calling 'selector.wakeup()' to reduce the wake-up
                        // overhead. (Selector.wakeup() is an expensive operation.)
                        //
                        // However, there is a race condition in this approach.
                        // The race condition is triggered when 'wakenUp' is set to
                        // true too early.
                        //
                        // 'wakenUp' is set to true too early if:
                        // 1) Selector is waken up between 'wakenUp.set(false)' and
                        //    'selector.select(...)'. (BAD)
                        // 2) Selector is waken up between 'selector.select(...)' and
                        //    'if (wakenUp.get()) { ... }'. (OK)
                        //
                        // In the first case, 'wakenUp' is set to true and the
                        // following 'selector.select(...)' will wake up immediately.
                        // Until 'wakenUp' is set to false again in the next round,
                        // 'wakenUp.compareAndSet(false, true)' will fail, and therefore
                        // any attempt to wake up the Selector will fail, too, causing
                        // the following 'selector.select(...)' call to block
                        // unnecessarily.
                        //
                        // To fix this problem, we wake up the selector again if wakenUp
                        // is true immediately after selector.select(...).
                        // It is inefficient in that it wakes up the selector for both
                        // the first case (BAD - wake-up required) and the second case
                        // (OK - no wake-up required).

                        if (_wakenUp.Get())
                        {
                            _selector.Wakeup();
                        }
                        // fall through
                        break;
                    default:
                        break;
                }
            }
            // CLR: a SocketException of Socket.Select is the IOException of Selector.select.
            catch (Exception e) when (e is System.IO.IOException or SocketException)
            {
                // If we receive an IOException here its because the Selector is messed up. Let's rebuild
                // the selector and retry. https://github.com/netty/netty/issues/8566
                RebuildSelector0();
                HandleLoopException(e);
                return 0;
            }

            _cancelledKeys = 0;
            _needsToSelectAgain = false;

            if (context.ShouldReportActiveIoTime())
            {
                // We start the timer after the blocking select() call has returned.
                long activeIoStartTimeNanos = NanoTime();
                handled = ProcessSelectedKeys();
                long activeIoEndTimeNanos = NanoTime();
                context.ReportActiveIoTime(unchecked(activeIoEndTimeNanos - activeIoStartTimeNanos));
            }
            else
            {
                handled = ProcessSelectedKeys();
            }
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception t)
        {
            HandleLoopException(t);
        }
        return handled;
    }

    private static void HandleLoopException(Exception t)
    {
        logger.Warn("Unexpected exception in the selector loop.", t);

        // Prevent possible consecutive immediate failures that lead to
        // excessive CPU consumption.
        try
        {
            Thread.Sleep(1000);
        }
        catch (ThreadInterruptedException)
        {
            // Ignore.
        }
    }

    // CLR: SystemTimer.NanoTime() is Common's System.nanoTime(); as in Java the handler does not use the executor's
    // ticker.
    private static long NanoTime() => Netty.NET.Common.SystemTimer.NanoTime();

    private int ProcessSelectedKeys()
    {
        return ProcessSelectedKeysOptimized();
    }

    public void Destroy()
    {
        try
        {
            _selector.Close();
        }
        catch (SocketException e)
        {
            logger.Warn("Failed to close a selector.", e);
        }
    }

    // CLR: processSelectedKeysPlain only serves a JDK selector whose key set could not be instrumented.
    private int ProcessSelectedKeysOptimized()
    {
        List<DefaultNioRegistration> selectedKeys = _selector.SelectedKeys;
        int handled = 0;
        for (int i = 0; i < selectedKeys.Count; ++i)
        {
            DefaultNioRegistration k = selectedKeys[i];
            // null out entry in the array to allow to have it GC'ed once the Channel close
            // See https://github.com/netty/netty/issues/2363
            selectedKeys[i] = null;

            ProcessSelectedKey(k);
            ++handled;

            if (_needsToSelectAgain)
            {
                // null out entries in the array to allow to have it GC'ed once the Channel close
                // See https://github.com/netty/netty/issues/2363
                for (int j = i + 1; j < selectedKeys.Count; j++)
                {
                    if (selectedKeys[j] != null)
                    {
                        selectedKeys[j].ReadyOps = 0;
                    }
                }
                selectedKeys.Clear();

                SelectAgain();
                i = -1;
            }
        }
        selectedKeys.Clear();
        return handled;
    }

    private void ProcessSelectedKey(DefaultNioRegistration k)
    {
        DefaultNioRegistration registration = k;
        int readyOps = registration.ReadyOps;
        registration.ReadyOps = 0;
        if (!registration.IsValid())
        {
            // CLR: the JDK drops a key cancelled by deregistration from the selected set at the next selection
            // operation; only a key invalidated by closing its channel reaches this close.
            if (registration.IsCanceled())
            {
                return;
            }
            try
            {
                registration.Handle().Close();
            }
            catch (Exception e)
            {
                logger.Debug("Exception during closing " + registration.Handle(), e);
            }
            return;
        }
        registration.Handle(readyOps);
    }

    public void PrepareToDestroy()
    {
        SelectAgain();
        ICollection<DefaultNioRegistration> keys = _selector.Keys();
        List<DefaultNioRegistration> registrations = new List<DefaultNioRegistration>(keys.Count);
        foreach (DefaultNioRegistration k in keys)
        {
            DefaultNioRegistration handle = k;
            registrations.Add(handle);
        }

        foreach (DefaultNioRegistration reg in registrations)
        {
            reg.Close();
        }
    }

    public void Wakeup()
    {
        if (!_executor.IsExecutorThread(Thread.CurrentThread) && _wakenUp.CompareAndSet(false, true))
        {
            _selector.Wakeup();
        }
    }

    public bool IsCompatible(Type handleType)
    {
        return typeof(INioIoHandle).IsAssignableFrom(handleType);
    }

    private void Select(IIoHandlerContext runner, bool oldWakenUp)
    {
        SocketSelector selector = _selector;
        int selectCnt = 0;
        long currentTimeNanos = NanoTime();
        long delayNanos = runner.DelayNanos(currentTimeNanos);
        // that's some special value which is used to indicate that no scheduled task is present.
        // we set the deadline to a bogus (unused) value for us to represent infinity
        long selectDeadLineNanos = long.MaxValue;
        if (delayNanos != long.MaxValue)
        {
            selectDeadLineNanos = unchecked(currentTimeNanos + runner.DelayNanos(currentTimeNanos));
        }
        for (;;)
        {
            long timeoutMillis;
            if (delayNanos != long.MaxValue)
            {
                long millisBeforeDeadline = MillisBeforeDeadline(selectDeadLineNanos, currentTimeNanos);
                if (millisBeforeDeadline <= 0)
                {
                    if (selectCnt == 0)
                    {
                        selector.SelectNow();
                        selectCnt = 1;
                    }
                    break;
                }
                timeoutMillis = millisBeforeDeadline;
            }
            else
            {
                // in NIO this means to block without any deadline
                timeoutMillis = 0;
            }
            // If a task was submitted when wakenUp value was true, the task didn't get a chance to call
            // Selector#wakeup. So we need to check task queue again before executing select operation.
            // If we don't, the task might be pended until select operation was timed out.
            // It might be pended until idle timeout if IdleStateHandler existed in pipeline.
            if (!runner.CanBlock() && _wakenUp.CompareAndSet(false, true))
            {
                selector.SelectNow();
                selectCnt = 1;
                break;
            }

            int selectedKeys = selector.Select(timeoutMillis);
            selectCnt++;

            if (selectedKeys != 0 || oldWakenUp || _wakenUp.Get() || !runner.CanBlock())
            {
                // - Selected something,
                // - waken up by user, or
                // - the task queue has a pending task.
                // - a scheduled task is ready for processing
                break;
            }
            // CLR: Thread.interrupted() has no counterpart in the select()-based wait; a CLR interrupt would raise
            // ThreadInterruptedException only from a blocking managed wait, which Socket.Select is not.

            long time = NanoTime();
            if (unchecked(time - timeoutMillis * 1_000_000L) >= currentTimeNanos)
            {
                // timeoutMillis elapsed without anything selected.
                selectCnt = 1;
            }
            else if (SELECTOR_AUTO_REBUILD_THRESHOLD > 0 &&
                    selectCnt >= SELECTOR_AUTO_REBUILD_THRESHOLD)
            {
                // The code exists in an extra method to ensure the method is not too big to inline as this
                // branch is not very likely to get hit very frequently.
                selector = SelectRebuildSelector(selectCnt);
                selectCnt = 1;
                break;
            }

            currentTimeNanos = time;
        }

        if (selectCnt > MIN_PREMATURE_SELECTOR_RETURNS)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Selector.select() returned prematurely {} times in a row for Selector {}.",
                        selectCnt - 1, selector);
            }
        }
    }

    private static long MillisBeforeDeadline(long selectDeadLineNanos, long currentTimeNanos)
    {
        System.Diagnostics.Debug.Assert(selectDeadLineNanos != long.MaxValue);
        long nanosBeforeDeadline = unchecked(selectDeadLineNanos - currentTimeNanos);
        // Prevent overflow when adding the rounding bias:
        // if we don't do this, it would appear as the deadline is already reached!
        if (nanosBeforeDeadline >= long.MaxValue - 500_000L)
        {
            return long.MaxValue / 1_000_000L;
        }
        // Add 500_000 to round up to the nearest millisecond.
        return (nanosBeforeDeadline + 500_000L) / 1_000_000L;
    }

    internal int SelectNow()
    {
        try
        {
            return _selector.SelectNow();
        }
        finally
        {
            // restore wakeup state if needed
            if (_wakenUp.Get())
            {
                _selector.Wakeup();
            }
        }
    }

    private SocketSelector SelectRebuildSelector(int selectCnt)
    {
        // The selector returned prematurely many times in a row.
        // Rebuild the selector to work around the problem.
        logger.Warn(
                "Selector.select() returned prematurely {} times in a row; rebuilding Selector {}.",
                selectCnt, _selector);

        RebuildSelector0();
        SocketSelector selector = _selector;

        // Select again to populate selectedKeys.
        selector.SelectNow();
        return selector;
    }

    private void SelectAgain()
    {
        _needsToSelectAgain = false;
        try
        {
            _selector.SelectNow();
        }
        catch (Exception t)
        {
            logger.Warn("Failed to update SelectionKeys.", t);
        }
    }

    /**
     * Returns a new {@link IoHandlerFactory} that creates {@link NioIoHandler} instances
     *
     * @return factory                  the {@link IoHandlerFactory}.
     */
    public static IIoHandlerFactory NewFactory()
    {
        return NewFactory(DefaultSelectStrategyFactory.INSTANCE);
    }

    /**
     * Returns a new {@link IoHandlerFactory} that creates {@link NioIoHandler} instances.
     *
     * @param selectorProvider          the {@link SelectorProvider} to use.
     * @param selectStrategyFactory     the {@link SelectStrategyFactory} to use.
     * @return factory                  the {@link IoHandlerFactory}.
     */
    // CLR: SelectorProvider is jvm-only-replaced, so newFactory(SelectorProvider) and the provider argument of this
    // overload have no counterpart.
    public static IIoHandlerFactory NewFactory(ISelectStrategyFactory selectStrategyFactory)
    {
        ObjectUtil.CheckNotNull(selectStrategyFactory, "selectStrategyFactory");
        return new Factory(selectStrategyFactory);
    }

    // CLR: the anonymous IoHandlerFactory becomes a nested class.
    private sealed class Factory(ISelectStrategyFactory selectStrategyFactory) : IIoHandlerFactory
    {
        public IIoHandler NewHandler(IThreadAwareExecutor executor)
        {
            return new NioIoHandler(executor, selectStrategyFactory.NewSelectStrategy());
        }

        public bool IsChangingThreadSupported()
        {
            return true;
        }
    }

    // CLR: the select()-based counterpart of the JDK Selector, used only by its owning NioIoHandler thread except
    // for Wakeup(). Registered sockets are selected through Socket.Select; the wakeup socket is a self-connected
    // loopback UDP socket that Wakeup() makes readable, as WindowsSelectorImpl makes its wakeup pipe readable.
    internal sealed class SocketSelector
    {
        private readonly Dictionary<Socket, DefaultNioRegistration> _keys = new(ReferenceEqualityComparer.Instance);
        private readonly List<Socket> _read = new();
        private readonly List<Socket> _write = new();
        private readonly List<Socket> _error = new();
        internal readonly List<DefaultNioRegistration> SelectedKeys = new();
        private readonly Socket _wakeupSocket;
        private readonly byte[] _wakeupBuffer = new byte[64];
        private readonly object _interruptLock = new();
        private bool _interruptTriggered;
        private bool _closed;
        private int _round;

        internal SocketSelector()
        {
            _wakeupSocket = NewWakeupSocket();
        }

        private static Socket NewWakeupSocket()
        {
            Socket socket = null;
            try
            {
                bool ipv4 = Socket.OSSupportsIPv4;
                socket = new Socket(ipv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6, SocketType.Dgram,
                    ProtocolType.Udp);
                socket.Bind(new IPEndPoint(ipv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback, 0));
                socket.Connect(socket.LocalEndPoint);
                socket.Blocking = false;
                return socket;
            }
            catch
            {
                socket?.Dispose();
                throw;
            }
        }

        internal ICollection<DefaultNioRegistration> Keys() => _keys.Values;

        internal DefaultNioRegistration KeyFor(Socket socket) =>
            _keys.TryGetValue(socket, out DefaultNioRegistration key) ? key : null;

        // CLR: InvalidOperationException stands for the IllegalArgumentException/CancelledKeyException a JDK
        // selector raises when a channel is registered twice.
        internal void Register(Socket socket, DefaultNioRegistration registration)
        {
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(NioIoHandler), "ClosedSelectorException");
            }
            if (!IsOpen(socket))
            {
                throw new ObjectDisposedException(nameof(Socket), "ClosedChannelException");
            }
            if (_keys.TryGetValue(socket, out DefaultNioRegistration existing) && !existing.IsCanceled())
            {
                throw new InvalidOperationException("The socket is already registered.");
            }
            _keys[socket] = registration;
        }

        internal void Cancel(Socket socket, DefaultNioRegistration registration)
        {
            if (_keys.TryGetValue(socket, out DefaultNioRegistration current) && ReferenceEquals(current, registration))
            {
                _keys.Remove(socket);
            }
        }

        internal static bool IsOpen(Socket socket)
        {
            try
            {
                return !socket.SafeHandle.IsClosed;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        internal int SelectNow() => DoSelect(0);

        // A timeout of 0 blocks without a deadline, as Selector.select(0).
        internal int Select(long timeoutMillis) => DoSelect(timeoutMillis == 0 ? -1
            : (int)Math.Min(int.MaxValue, timeoutMillis * 1000L));

        private int DoSelect(int microSeconds)
        {
            if (_closed)
            {
                throw new ObjectDisposedException(nameof(NioIoHandler), "ClosedSelectorException");
            }
            lock (_interruptLock)
            {
                // A pending wakeup makes this selection return at once without blocking, as in WindowsSelectorImpl.
                if (_interruptTriggered)
                {
                    ResetWakeupSocket();
                    return 0;
                }
            }
            _read.Clear();
            _write.Clear();
            _error.Clear();
            _read.Add(_wakeupSocket);
            foreach (KeyValuePair<Socket, DefaultNioRegistration> entry in _keys)
            {
                DefaultNioRegistration key = entry.Value;
                // An invalid key (closed socket) is never selected.
                if (key.IsCanceled() || !IsOpen(entry.Key))
                {
                    continue;
                }
                int ops = key.InterestOps;
                if ((ops & (NioIoOps.OP_READ | NioIoOps.OP_ACCEPT)) != 0)
                {
                    _read.Add(entry.Key);
                }
                if ((ops & (NioIoOps.OP_WRITE | NioIoOps.OP_CONNECT)) != 0)
                {
                    _write.Add(entry.Key);
                }
                if ((ops & NioIoOps.OP_CONNECT) != 0)
                {
                    // Winsock reports a failed non-blocking connect in exceptfds, not in writefds.
                    _error.Add(entry.Key);
                }
            }
            Socket.Select(_read, _write.Count == 0 ? null : _write, _error.Count == 0 ? null : _error, microSeconds);
            int round = unchecked(++_round);
            int updated = 0;
            bool woken = false;
            foreach (Socket socket in _read)
            {
                if (ReferenceEquals(socket, _wakeupSocket))
                {
                    woken = true;
                    continue;
                }
                updated += Ready(socket, NioIoOps.OP_READ | NioIoOps.OP_ACCEPT, round);
            }
            foreach (Socket socket in _write)
            {
                updated += Ready(socket, NioIoOps.OP_WRITE | NioIoOps.OP_CONNECT, round);
            }
            foreach (Socket socket in _error)
            {
                updated += Ready(socket, NioIoOps.OP_CONNECT, round);
            }
            if (woken)
            {
                lock (_interruptLock)
                {
                    ResetWakeupSocket();
                }
            }
            return updated;
        }

        private int Ready(Socket socket, int mask, int round)
        {
            if (!_keys.TryGetValue(socket, out DefaultNioRegistration key))
            {
                return 0;
            }
            int ready = key.InterestOps & mask;
            if (ready == 0)
            {
                return 0;
            }
            key.ReadyOps |= ready;
            if (key.SelectedRound != round)
            {
                key.SelectedRound = round;
                if (!SelectedKeys.Contains(key))
                {
                    SelectedKeys.Add(key);
                }
                return 1;
            }
            return 0;
        }

        internal void Wakeup()
        {
            lock (_interruptLock)
            {
                if (_interruptTriggered || _closed)
                {
                    return;
                }
                try
                {
                    _wakeupSocket.Send(_wakeupBuffer, 0, 1, SocketFlags.None, out SocketError _);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                _interruptTriggered = true;
            }
        }

        // Must be called with _interruptLock held.
        private void ResetWakeupSocket()
        {
            if (!_interruptTriggered)
            {
                return;
            }
            while (_wakeupSocket.Available > 0 &&
                _wakeupSocket.Receive(_wakeupBuffer, 0, _wakeupBuffer.Length, SocketFlags.None, out SocketError error) > 0 &&
                error == SocketError.Success)
            {
            }
            _interruptTriggered = false;
        }

        internal void Close()
        {
            lock (_interruptLock)
            {
                if (_closed)
                {
                    return;
                }
                _closed = true;
            }
            _keys.Clear();
            SelectedKeys.Clear();
            _wakeupSocket.Dispose();
        }

        public override string ToString() => "SocketSelector@" + GetHashCode().ToString("x");
    }
}
