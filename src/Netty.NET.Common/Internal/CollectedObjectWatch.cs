using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Netty.NET.Common.Internal;

// A conditional value follows the referent's lifetime without rooting its key.
// Registrations never strongly reference that value or the watched object.
// Notifications only enqueue internal work; user callbacks run on a normal thread.
internal static class CollectedObjectWatch
{
    private static readonly ConditionalWeakTable<object, Watch> watches = new();

    internal static Registration register(object resource, Action notification)
    {
        ArgumentNullException.ThrowIfNull(resource);
        Watch watch = watches.GetValue(resource, static _ => new Watch());
        var registration = new Registration(watch, notification);
        watch.add(registration);
        GC.KeepAlive(resource);
        return registration;
    }

    internal sealed class Registration
    {
        private readonly WeakReference<Watch> owner;
        private Action notification;
        internal Registration Next;
        internal Registration(Watch watch, Action notification)
        {
            owner = new WeakReference<Watch>(watch);
            this.notification = notification;
        }
        internal void cancel()
        {
            Interlocked.Exchange(ref notification, null);
            if (owner.TryGetTarget(out Watch watch)) watch.remove(this);
        }
        internal void signal()
        {
            Action action = Interlocked.Exchange(ref notification, null);
            try { action?.Invoke(); }
            catch (Exception) { /* A finalizer must not terminate the CLR process. */ }
        }
    }

    internal sealed class Watch
    {
        private Registration head;
        private bool suppressed;
        internal void add(Registration registration)
        {
            using var held = UninterruptibleMonitor.enter(this);
            if (suppressed) { GC.ReRegisterForFinalize(this); suppressed = false; }
            registration.Next = head;
            head = registration;
        }
        internal void remove(Registration registration)
        {
            using var held = UninterruptibleMonitor.enter(this);
            Registration previous = null;
            for (Registration current = head; current != null; current = current.Next)
            {
                if (ReferenceEquals(current, registration))
                {
                    if (previous == null) head = current.Next;
                    else previous.Next = current.Next;
                    current.Next = null;
                    break;
                }
                previous = current;
            }
            if (head == null && !suppressed) { GC.SuppressFinalize(this); suppressed = true; }
        }
        ~Watch()
        {
            Registration registrations;
            lock (this) { registrations = head; head = null; }
            while (registrations != null)
            {
                Registration next = registrations.Next;
                registrations.Next = null;
                registrations.signal();
                registrations = next;
            }
        }
    }
}
