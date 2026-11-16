using System;
using System.IO;

namespace Netty.NET.Transport.Channels;

/// <summary>
/// CLR counterpart of <c>java.nio.channels.ClosedChannelException</c>, which Netty throws and extends.
/// </summary>
// CLR: the JDK type is an IOException thrown when an I/O operation is invoked on a closed channel and has only a
// no-argument constructor (no message). The BCL has no such type, so this one keeps the same base type and shape.
// Throwable.initCause has no Exception counterpart (InnerException is read-only and set at construction), so derived
// types that attach a cause pass it to the protected constructor.
public class ClosedChannelException : IOException
{
    public ClosedChannelException() : base(null, null) { }

    protected ClosedChannelException(Exception cause) : base(null, cause) { }
}
