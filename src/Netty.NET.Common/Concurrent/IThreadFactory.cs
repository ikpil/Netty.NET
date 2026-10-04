using System;
using System.Threading;

namespace Netty.NET.Common.Concurrent;

public interface IThreadFactory
{
    Thread NewThread(Action r);
}