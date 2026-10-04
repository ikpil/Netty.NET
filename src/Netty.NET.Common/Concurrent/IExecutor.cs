using System;

namespace Netty.NET.Common.Concurrent;

// CLR: fire-and-forget admission uses Action; result-bearing work uses SubmitAsync.
public interface IExecutor
{
    /**
      * Executes the given command at some time in the future.  The command
      * may execute in a new thread, in a pooled thread, or in the calling
      * thread, at the discretion of the {@code IExecutor} implementation.
      *
      * @param command the runnable task
      * @throws RejectedExecutionException if this task cannot be
      * accepted for execution
      * @throws NullReferenceException if command is null
      */
    void Execute(Action command);
}
