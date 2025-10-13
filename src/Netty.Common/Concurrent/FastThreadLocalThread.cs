using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class FastThreadLocalThread
{
    private IRunnable _task;
    
    public FastThreadLocalThread(IRunnable task)
    {
        _task = task;
        throw new NotImplementedException();
    }
    
    public static bool currentThreadHasFastThreadLocal()
    {
        return true;
    }
}