namespace Netty.NET.Common.Concurrent;

public interface IExecutor
{
    /**
      * Executes the given command at some time in the future.  The command
      * may execute in a new thread, in a pooled thread, or in the calling
      * thread, at the discretion of the {@code Executor} implementation.
      *
      * @param command the runnable task
      * @throws RejectedExecutionException if this task cannot be
      * accepted for execution
      * @throws NullPointerException if command is null
      */
    void execute(IRunnable command);
}