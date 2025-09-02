using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class QueueingCallableTaskNode<T> : QueueingTaskNode<T>
{
    private readonly ICallable<T> _func;

    public QueueingCallableTaskNode(ICallable<T> func)
        : this(func, CancellationToken.None)
    {
    }

    public QueueingCallableTaskNode(ICallable<T> func, CancellationToken cancellationToken)
        : base(new TaskCompletionSource<T>(), cancellationToken)
    {
        _func = func;
    }

    protected override T call()
    {
        return _func.call();
    }
}