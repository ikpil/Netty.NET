using System.Threading;

namespace Netty.NET.Common.Concurrent;

public interface IThreadFactory
{
    Thread newThread(IRunnable r);
}