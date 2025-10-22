using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

internal sealed class AccountingThreadFactory : IThreadFactory
{
    private readonly IThreadFactory _factory;
    private readonly ISet<Thread> _threads;

    private AccountingThreadFactory(IThreadFactory factory, ISet<Thread> threads)
    {
        _factory = factory;
        _threads = threads;
    }

    public Thread newThread([NotNull] IRunnable r)
    {
        return _factory.newThread(Runnables.Create(() =>
        {
            _threads.Add(Thread.CurrentThread);
            try
            {
                r.run();
            }
            finally
            {
                _threads.Remove(Thread.CurrentThread);
            }
        }));
    }
}