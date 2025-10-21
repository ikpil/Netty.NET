using System;

namespace Netty.NET.Common;

public class CauseHolder
{
    public readonly Exception cause;

    public CauseHolder(Exception cause)
    {
        this.cause = cause;
    }
}