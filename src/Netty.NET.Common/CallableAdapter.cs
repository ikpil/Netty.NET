using Netty.NET.Common.Functional;

namespace Netty.NET.Common;

public class CallableAdapter<T> : ICallable<T>
{
    private readonly IRunnable _task;
    private readonly T _result;

    public CallableAdapter(IRunnable task, T result)
    {
        _task = task;
        _result = result;
    }

    public T call()
    {
        _task.run();
        return _result;
    }

    public override string ToString()
    {
        return "Callable(_task: " + _task + ", _result: " + _result + ')';
    }
}