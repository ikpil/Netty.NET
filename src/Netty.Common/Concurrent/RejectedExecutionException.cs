using System;

namespace Netty.NET.Common.Concurrent;

public class RejectedExecutionException : Exception
{
    public RejectedExecutionException()
    {
    }

    public RejectedExecutionException(string message)
        : base(message)
    {
    }
}