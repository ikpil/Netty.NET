using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

/// <summary>Task-based submission to Netty executors and groups using native delegates and cancellation.</summary>
/// <remarks>
/// Groups select a child for each submission. NonSticky group submission delegates to the
/// underlying group, as in Netty; submitting to a child returned by next() uses that child.
/// </remarks>
public static class EventExecutorExtensions
{
    /// <summary>Queues a synchronous action on the executor and returns its completion.</summary>
    public static Task SubmitAsync(this IEventExecutorGroup executor, Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SubmitCoreAsync<object>(executor, _ => { action(); return null; }, cancellationToken);
    }

    /// <summary>Queues a synchronous function on the executor and returns its result.</summary>
    public static Task<T> SubmitAsync<T>(this IEventExecutorGroup executor, Func<T> function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return SubmitCoreAsync(executor, _ => function(), cancellationToken);
    }

    /// <summary>Queues an action that can cooperatively observe cancellation after it starts.</summary>
    public static Task SubmitAsync(this IEventExecutorGroup executor, Action<CancellationToken> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return SubmitCoreAsync<object>(executor, token => { action(token); return null; }, cancellationToken);
    }

    /// <summary>Queues a function that can cooperatively observe cancellation after it starts.</summary>
    /// <remarks>
    /// Cancellation before execution prevents the delegate from running. Once execution starts,
    /// cancellation is cooperative: a normal return succeeds; an OperationCanceledException
    /// with the requested submission token cancels; other exceptions fault the Task.
    /// The caller's ExecutionContext flows unless flow is suppressed, and changes are scoped
    /// to the invocation. Awaiting this Task does not move the caller onto the executor.
    /// Submission rejection is reported through the returned Task. Argument errors are synchronous.
    /// </remarks>
    public static Task<T> SubmitAsync<T>(this IEventExecutorGroup executor, Func<CancellationToken, T> function,
        CancellationToken cancellationToken = default)
        => SubmitCoreAsync(executor, function, cancellationToken);

    private static Task<T> SubmitCoreAsync<T>(IEventExecutorGroup executor, Func<CancellationToken, T> function,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(function);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
        var submitted = new SubmittedTask<T>(function, cancellationToken);
        if (!submitted.Task.IsCompleted)
        {
            try
            {
                IEventExecutorGroup targetGroup = executor;
                while (targetGroup is NonStickyEventExecutorGroup nonSticky)
                    targetGroup = nonSticky.DelegatedGroup;
                IEventExecutor target = targetGroup as IEventExecutor ?? targetGroup.next();
                target.execute(submitted);
            }
            catch (Exception error) { submitted.Reject(error); }
        }
        return submitted.Task;
    }

    /// <summary>Starts an asynchronous delegate on the executor and observes its entire completion.</summary>
    /// <remarks>
    /// The executor orders delegate invocation, not the entire asynchronous operation.
    /// After suspension, continuations follow the delegate's normal await context; they are
    /// not guaranteed to run on the executor. Submit subsequent executor-owned work explicitly.
    /// Returning a null Task faults the submission with InvalidOperationException.
    /// </remarks>
    public static Task SubmitAsync(this IEventExecutorGroup executor, Func<Task> function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return SubmitCoreAsync(executor, _ => function() ?? throw new InvalidOperationException("The submitted delegate returned a null Task."), cancellationToken).Unwrap();
    }

    /// <summary>Starts an asynchronous function on the executor and unwraps its eventual result.</summary>
    public static Task<T> SubmitAsync<T>(this IEventExecutorGroup executor, Func<Task<T>> function, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return SubmitCoreAsync(executor, _ => function() ?? throw new InvalidOperationException("The submitted delegate returned a null Task."), cancellationToken).Unwrap();
    }

    /// <summary>Starts an asynchronous action with the submission token and observes its completion.</summary>
    public static Task SubmitAsync(this IEventExecutorGroup executor, Func<CancellationToken, Task> function,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return SubmitCoreAsync(executor, token => function(token) ?? throw new InvalidOperationException("The submitted delegate returned a null Task."), cancellationToken).Unwrap();
    }

    /// <summary>Starts an asynchronous function with the submission token and unwraps its result.</summary>
    public static Task<T> SubmitAsync<T>(this IEventExecutorGroup executor, Func<CancellationToken, Task<T>> function,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return SubmitCoreAsync(executor, token => function(token) ?? throw new InvalidOperationException("The submitted delegate returned a null Task."), cancellationToken).Unwrap();
    }

    // This CLR work item implements the execution boundary used by upstream
    // PromiseTask.run()/setUncancellableInternal(), without a Java Future or Runnable
    // in the consumer API. TaskCompletionSource alone owns the terminal result.
    private sealed class SubmittedTask<T> : ICancelableNativeSubmission
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _registration;
        private Func<CancellationToken, T> _function;
        private ExecutionContext _context;
        private Action _removeCanceled;
        // A single claim selects invocation, pre-start cancellation, or rejection.
        // It describes ownership of the invocation, not a second completion state.
        private int _claimed;

        internal SubmittedTask(Func<CancellationToken, T> function, CancellationToken cancellationToken)
        {
            _function = function;
            _context = ExecutionContext.Capture();
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.UnsafeRegister(static state => ((SubmittedTask<T>)state).CancelBeforeStart(), this);
            // Registration can synchronously call CancelBeforeStart before its
            // returned handle is assigned. The constructor and paths invoked after
            // construction unregister that immutable handle, avoiding a publication race.
            if (Task.IsCompleted) _registration.Unregister();
        }

        internal Task<T> Task => _completion.Task;
        public bool IsCanceled => Task.IsCanceled;

        public void SetCancellationRemoval(Action remove)
        {
            Volatile.Write(ref _removeCanceled, remove);
            // Cancellation may have won before binding, or race the publication.
            // Either binder or canceler consumes this single membership hook.
            if (Task.IsCanceled) RemoveCanceled();
            else if (Task.IsCompleted) Interlocked.Exchange(ref _removeCanceled, null);
        }

        public void CancelForShutdown()
        {
            CancelBeforeStart();
            _registration.Unregister();
        }

        private void CancelBeforeStart()
        {
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
            ReleaseInvocation();
            _completion.SetCanceled(_cancellationToken);
            RemoveCanceled();
        }

        private void RemoveCanceled() => Interlocked.Exchange(ref _removeCanceled, null)?.Invoke();

        public void Reject(Exception error)
        {
            Interlocked.Exchange(ref _removeCanceled, null);
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) == 0)
            {
                ReleaseInvocation();
                _completion.SetException(error);
            }
            _registration.Unregister();
        }

        public void run()
        {
            bool execute = Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
            Interlocked.Exchange(ref _removeCanceled, null);
            // Ownership is already decided by the claim. Never block an event
            // loop waiting for a cancellation callback that can no longer win.
            _registration.Unregister();
            if (!execute) return;
            try
            {
                ExecutionContext context = Interlocked.Exchange(ref _context, null) ?? CaptureExecutorContext();
                ExecutionContext.Run(context, static state => ((SubmittedTask<T>)state).Invoke(), this);
            }
            catch (OperationCanceledException error) when (_cancellationToken.IsCancellationRequested &&
                error.CancellationToken == _cancellationToken)
            {
                _completion.SetCanceled(_cancellationToken);
            }
            catch (Exception error) { _completion.SetException(error); }
            finally { ReleaseInvocation(); }
        }

        private void Invoke()
        {
            Func<CancellationToken, T> function = Interlocked.Exchange(ref _function, null);
            _completion.SetResult(function(_cancellationToken));
        }

        private void ReleaseInvocation()
        {
            Interlocked.Exchange(ref _function, null);
            Interlocked.Exchange(ref _context, null);
        }

        private static ExecutionContext CaptureExecutorContext()
        {
            // Suppressed submission flow means use the executor's ambient values,
            // while still isolating changes made by this invocation. Capture needs
            // flow temporarily enabled even for an executor running under suppression.
            bool suppressed = ExecutionContext.IsFlowSuppressed();
            if (!suppressed) return ExecutionContext.Capture();
            ExecutionContext.RestoreFlow();
            try { return ExecutionContext.Capture(); }
            finally { ExecutionContext.SuppressFlow(); }
        }
    }
}
