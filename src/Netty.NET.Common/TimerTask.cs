using System;

namespace Netty.NET.Common;

public class TimerTask : ITimerTask
{
    private readonly Action<ITimeout> _run;
    private readonly Action<ITimeout> _canceled;

    public static TimerTask Create(Action<ITimeout> run, Action<ITimeout> canceled = null)
    {
        return new TimerTask(run, canceled);
    }

    public TimerTask(Action<ITimeout> run, Action<ITimeout> canceled)
    {
        _run = run;
        _canceled = canceled;
    }

    public void run(ITimeout timeout)
    {
        _run.Invoke(timeout);
    }

    public void cancelled(ITimeout timeout)
    {
        _canceled?.Invoke(timeout);
    }
}