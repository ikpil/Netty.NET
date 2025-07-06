using System;

namespace Netty.NET.Common.Internal.Logging
{
    public interface IInternalLoggerFactory : IDisposable
    {
        IInternalLogger CreateLogger(string categoryName);
        void AddProvider(IInternalLoggerProvider provider);
    }
}