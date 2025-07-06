using System;

namespace Netty.NET.Common.Internal.Logging
{
    public interface ILoggerProvider : IDisposable
    {
        ILogger CreateLogger(string categoryName);
    }
}