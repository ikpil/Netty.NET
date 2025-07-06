using System;

namespace Netty.NET.Common.Internal.Logging
{
    public interface ILoggerFactory : IDisposable
    {
        ILogger CreateLogger(string categoryName);
        void AddProvider(ILoggerProvider provider);
    }
}