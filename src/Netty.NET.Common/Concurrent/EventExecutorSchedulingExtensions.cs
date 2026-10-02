using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

/// <summary>Task-based deadline and periodic scheduling on the executor's own queue.</summary>
public static class EventExecutorSchedulingExtensions
{
    /// <summary>Invokes an action at or after the delay. A negative delay means immediate eligibility.</summary>
    public static Task ScheduleAsync(this IEventExecutorGroup executor, Action action, TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ScheduleCore<object>(executor, _ => { action(); return null; }, delay, 0, cancellationToken);
    }

    public static Task<T> ScheduleAsync<T>(this IEventExecutorGroup executor, Func<T> function, TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return ScheduleCore(executor, _ => function(), delay, 0, cancellationToken);
    }

    public static Task ScheduleAsync(this IEventExecutorGroup executor, Action<CancellationToken> action, TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ScheduleCore<object>(executor, token => { action(token); return null; }, delay, 0, cancellationToken);
    }

    /// <remarks>
    /// Before invocation, cancellation prevents execution. Once a one-shot invocation starts,
    /// cancellation is cooperative. A normal return succeeds; a matching requested-token
    /// OperationCanceledException cancels; other exceptions fault. No worker is interrupted.
    /// ExecutionContext flows unless suppressed; changes are isolated to the invocation.
    /// Awaiting the result does not confer event-loop affinity. Rejection faults the Task.
    /// </remarks>
    public static Task<T> ScheduleAsync<T>(this IEventExecutorGroup executor, Func<CancellationToken, T> function,
        TimeSpan delay, CancellationToken cancellationToken = default)
        => ScheduleCore(executor, function, delay, 0, cancellationToken);

    /// <summary>Starts an asynchronous operation after the delay and observes its final completion.</summary>
    /// <remarks>Only invocation is ordered by the executor. After suspension, normal await context rules apply.</remarks>
    public static Task ScheduleAsync(this IEventExecutorGroup executor, Func<Task> function, TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return ScheduleCore(executor, _ => function() ?? throw new InvalidOperationException("The scheduled delegate returned a null Task."),
            delay, 0, cancellationToken).Unwrap();
    }

    public static Task<T> ScheduleAsync<T>(this IEventExecutorGroup executor, Func<Task<T>> function, TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return ScheduleCore(executor, _ => function() ?? throw new InvalidOperationException("The scheduled delegate returned a null Task."),
            delay, 0, cancellationToken).Unwrap();
    }

    public static Task ScheduleAsync(this IEventExecutorGroup executor, Func<CancellationToken, Task> function,
        TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return ScheduleCore(executor, token => function(token) ?? throw new InvalidOperationException("The scheduled delegate returned a null Task."),
            delay, 0, cancellationToken).Unwrap();
    }

    public static Task<T> ScheduleAsync<T>(this IEventExecutorGroup executor, Func<CancellationToken, Task<T>> function,
        TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return ScheduleCore(executor, token => function(token) ?? throw new InvalidOperationException("The scheduled delegate returned a null Task."),
            delay, 0, cancellationToken).Unwrap();
    }

    /// <summary>Repeats a synchronous action at fixed deadlines. The Task completes on cancellation or failure.</summary>
    /// <remarks>Invocations of the same repeating work never overlap. Cancellation can finish the Task while a current callback returns.</remarks>
    public static Task ScheduleAtFixedRateAsync(this IEventExecutorGroup executor, Action action, TimeSpan initialDelay,
        TimeSpan period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SchedulePeriodic(executor, _ => action(), initialDelay, period, false, cancellationToken);
    }

    public static Task ScheduleAtFixedRateAsync(this IEventExecutorGroup executor, Action<CancellationToken> action,
        TimeSpan initialDelay, TimeSpan period, CancellationToken cancellationToken = default)
        => SchedulePeriodic(executor, action, initialDelay, period, false, cancellationToken);

    /// <summary>Repeats after a delay measured from each synchronous callback's return.</summary>
    public static Task ScheduleWithFixedDelayAsync(this IEventExecutorGroup executor, Action action, TimeSpan initialDelay,
        TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SchedulePeriodic(executor, _ => action(), initialDelay, delay, true, cancellationToken);
    }

    public static Task ScheduleWithFixedDelayAsync(this IEventExecutorGroup executor, Action<CancellationToken> action,
        TimeSpan initialDelay, TimeSpan delay, CancellationToken cancellationToken = default)
        => SchedulePeriodic(executor, action, initialDelay, delay, true, cancellationToken);

    private static Task SchedulePeriodic(IEventExecutorGroup executor, Action<CancellationToken> action,
        TimeSpan initialDelay, TimeSpan interval, bool fixedDelay, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay));
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        long period = AbstractScheduledEventExecutor.toNanos(interval);
        return ScheduleCore<object>(executor, ct => { action(ct); return null; }, initialDelay,
            fixedDelay ? -period : period, token);
    }

    private static Task<T> ScheduleCore<T>(IEventExecutorGroup group, Func<CancellationToken, T> function,
        TimeSpan delay, long period, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(function);
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        // A NonSticky group directly delegates scheduling to its underlying group;
        // its ordered child wrapper deliberately does not support scheduling.
        while (group is NonStickyEventExecutorGroup wrapper) group = wrapper.DelegatedGroup;
        IEventExecutor executor = group is IEventExecutor child ? child : group.next();
        if (executor is AbstractScheduledEventExecutor ordered)
            return ordered.ScheduleNative(function, delay, period, token);
        if (executor is UnorderedThreadPoolEventExecutor unordered)
            return unordered.ScheduleNative(function, delay, period, token);
        return Task.FromException<T>(new NotSupportedException("This executor does not support deadline scheduling."));
    }
}
