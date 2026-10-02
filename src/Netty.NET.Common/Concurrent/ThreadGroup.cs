using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// CLR adaptation of the JDK ThreadGroup identity used by Netty thread factories.
// The CLR has no native groups. Weak thread metadata preserves explicit groups
// and creator inheritance for threads created through these Netty adapters.
public sealed class ThreadGroup
{
    private static readonly ConditionalWeakTable<Thread, ThreadGroup> Groups = new();
    private static readonly ThreadGroup Root = new("main");
    private readonly string name;

    public ThreadGroup(string name)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string getName() => name;
    public static ThreadGroup currentThreadGroup() => getThreadGroup(Thread.CurrentThread);
    public static ThreadGroup getThreadGroup(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return Groups.TryGetValue(thread, out var group) ? group : Root;
    }

    internal static void assign(Thread thread, ThreadGroup group) =>
        Groups.Add(thread, group ?? currentThreadGroup());

    // CLR counterpart of new Thread(group, runnable), without fast-local behavior.
    public Thread newThread(IRunnable runnable)
    {
        ArgumentNullException.ThrowIfNull(runnable);
        var thread = new Thread(runnable.run);
        assign(thread, this);
        return thread;
    }
}
