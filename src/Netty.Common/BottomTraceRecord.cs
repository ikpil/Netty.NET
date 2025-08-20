using System;

namespace Netty.NET.Common;

public class BottomTraceRecord : TraceRecord
{
    // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
    // Classloader.
    // See https://github.com/netty/netty/pull/10691
    public Exception fillInStackTrace()
    {
        return this;
    }
}