using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public abstract class QueueingTaskNode<T> : IRunnable
{
    private readonly TaskCompletionSource<T> _promise;
    private readonly CancellationToken _cancellationToken;
    private bool _mayInterruptIfRunning;

    protected QueueingTaskNode(TaskCompletionSource<T> promise, CancellationToken cancellationToken)
    {
        _promise = promise;
        _cancellationToken = cancellationToken;
    }

    public Task<T> Completion => _promise.Task;
    public bool IsCompleted => _promise.Task.IsCompleted;

    public void cancel(bool mayInterruptIfRunning)
    {
        _mayInterruptIfRunning = mayInterruptIfRunning;
    }

    public void run()
    {
        if (_mayInterruptIfRunning || _cancellationToken.IsCancellationRequested)
        {
            _promise.TrySetCanceled();
            return;
        }

        try
        {
            T result = call();
            _promise.TrySetResult(result);
        }
        catch (Exception ex)
        {
            // todo: handle fatal
            _promise.TrySetException(ex);
        }
    }

    protected abstract T call();
}