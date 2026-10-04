using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// Internal queue payloads carry cancellation, deadline and runner ownership.
// Their exact Action envelope crosses the native virtual hook without exposing
// a second public Execute API or changing the queue's membership identity.
internal sealed class ExecutorWork
{
    private readonly IRunnable _work;
    private readonly Action _entry;

    private ExecutorWork(IRunnable work)
    {
        _work = work;
        _entry = Invoke;
    }

    private void Invoke() => _work.Run();

    internal static Action Wrap(IRunnable work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return new ExecutorWork(work)._entry;
    }

    internal static IRunnable Unwrap(Action command, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(command, parameterName);
        // A multicast or newly composed delegate is ordinary caller work. Never
        // infer queue ownership from an arbitrary Action target or method name.
        if (command.Target is ExecutorWork entry && ReferenceEquals(command, entry._entry)) return entry._work;
        return Runnables.Create(command);
    }

    internal static void Dispatch(IExecutor executor, IRunnable work) => executor.Execute(Wrap(work));
}

// Real common producers use this bridge; external consumers see only Action.
internal static class ExecutorWorkExtensions
{
    internal static void Execute(this IExecutor executor, IRunnable work) => ExecutorWork.Dispatch(executor, work);
}
