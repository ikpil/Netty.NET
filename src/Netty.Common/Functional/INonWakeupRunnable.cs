using System;

namespace Netty.NET.Common.Functional;

/**
 * @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
 */
[Obsolete]
internal interface INonWakeupRunnable : ILazyRunnable
{
        
}

