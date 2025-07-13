using System;

namespace Netty.NET.Common.Internal.Logging
{
    public interface IInternalLoggerFactory : IDisposable
    {
        IInternalLogger newInstance(string categoryName);
    }
}