using System;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class StacklessCancellationException : TaskCanceledException 
{
    private StacklessCancellationException(string msg) : base(msg)
    {
        
    }

    // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
    // Classloader.
    public Exception fillInStackTrace() 
    {
        return this;
    }

    public static StacklessCancellationException newInstance(Type clazz, string method) {
        return ThrowableUtil.unknownStackTrace(msg => new StacklessCancellationException(msg), clazz, method);
    }
}
