using System;
using System.Threading.Tasks;

namespace Netty.NET.Common;

public class LeanCancellationException : TaskCanceledException
{
    // Suppress a warning since the method doesn't need synchronization
    public Exception FillInStackTrace()
    {
        //setStackTrace(CANCELLATION_STACK);
        return this;
    }

    public override string ToString()
    {
        // CLR: CancellationException.class.getName() names the CLR counterpart type.
        return typeof(TaskCanceledException).FullName;
    }
}