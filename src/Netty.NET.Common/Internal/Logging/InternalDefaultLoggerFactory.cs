using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Netty.NET.Common.Internal.Logging
{
    internal class InternalDefaultLoggerFactory : IInternalLoggerFactory
    {
        private readonly object _sync = new object();
        private readonly ConcurrentDictionary<string, InternalDefaultLogger> _loggers;
        private volatile bool _disposed;

        public InternalDefaultLoggerFactory()
        {
            _loggers = new ConcurrentDictionary<string, InternalDefaultLogger>();
        }

        protected virtual bool CheckDisposed() => _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // ...
            }
        }

        public IInternalLogger newInstance(string categoryName)
        {
            if (CheckDisposed())
            {
                throw new ObjectDisposedException(nameof(InternalDefaultLoggerFactory));
            }

            if (!_loggers.TryGetValue(categoryName, out InternalDefaultLogger logger))
            {
                lock (_sync)
                {
                    if (!_loggers.TryGetValue(categoryName, out logger))
                    {
                        logger = new InternalDefaultLogger(categoryName);
                        _loggers[categoryName] = logger;
                    }
                }
            }

            return logger;
        }
    }
}