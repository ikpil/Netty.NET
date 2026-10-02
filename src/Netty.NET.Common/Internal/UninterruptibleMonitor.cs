using System;
using System.Threading;

namespace Netty.NET.Common.Internal;

// CLR counterpart of Java synchronized/ReentrantLock.lock entry. CLR monitor
// contention can consume Thread.Interrupt and throw; Java non-interruptible entry
// acquires the lock while retaining that flag for a later interruptible wait.
internal static class UninterruptibleMonitor
{
    internal static Lease enter(object monitor)
    {
        bool interrupted = false;
        for (;;)
        {
            try { Monitor.Enter(monitor); break; }
            catch (ThreadInterruptedException) { interrupted = true; }
        }
        if (interrupted) Thread.CurrentThread.Interrupt();
        return new Lease(monitor);
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly object monitor;
        internal Lease(object monitor) => this.monitor = monitor;
        public void Dispose() => Monitor.Exit(monitor);
    }
}
