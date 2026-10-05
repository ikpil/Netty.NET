using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// Internal queue payloads carry cancellation, deadline and runner ownership.
// Their exact Action envelope crosses the native virtual hook without exposing
// a second public Execute API or changing the queue's membership identity.
internal sealed class ExecutorWork
{
    private readonly INativeSubmission _work;
    private readonly Action _entry;

    private ExecutorWork(INativeSubmission work)
    {
        _work = work;
        _entry = Invoke;
    }

    private void Invoke() => _work.Run();

    internal static Action Wrap(INativeSubmission work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return new ExecutorWork(work)._entry;
    }

    internal static INativeSubmission GetNativeSubmission(Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        // A multicast or newly composed delegate is ordinary caller work. Never
        // infer queue ownership from an arbitrary Action target or method name.
        if (command.Target is ExecutorWork entry && ReferenceEquals(command, entry._entry)) return entry._work;
        return null;
    }

    internal static void Dispatch(IExecutor executor, INativeSubmission work) => executor.Execute(Wrap(work));
}

// Real common producers use this bridge; external consumers see only Action.
internal static class ExecutorWorkExtensions
{
    internal static void Execute(this IExecutor executor, INativeSubmission work) => ExecutorWork.Dispatch(executor, work);

    // Remaining Runnable callers need invocation only, without native ownership metadata.
    internal static void Execute(this IExecutor executor, IRunnable work)
    {
        ArgumentNullException.ThrowIfNull(work);
        executor.Execute(work.Run);
    }
}
