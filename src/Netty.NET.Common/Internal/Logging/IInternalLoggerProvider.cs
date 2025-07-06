using System;

namespace Netty.NET.Common.Internal.Logging
{
    public interface IInternalLoggerProvider : IDisposable
    {
        IInternalLogger CreateLogger(string categoryName);
    }
}