using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class QueueingRunnableTaskNode<T> : QueueingTaskNode<T>
{
    private readonly IRunnable _func;
    private readonly T _value;

    public QueueingRunnableTaskNode(IRunnable func, T value)
        : this(func, value, CancellationToken.None)
    {
    }

    public QueueingRunnableTaskNode(IRunnable func, T value, CancellationToken cancellationToken)
        : base(new TaskCompletionSource<T>(), cancellationToken)
    {
        _func = func;
        _value = value;
    }

    protected override T call()
    {
        _func.run();
        return _value;
    }
}