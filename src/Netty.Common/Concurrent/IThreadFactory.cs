using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public interface IThreadFactory
{
    Thread newThread(IRunnable r);
}