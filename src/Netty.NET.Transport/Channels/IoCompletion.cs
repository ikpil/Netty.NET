using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels;

internal sealed class IoCompletion(Func<(Task Completion, bool Handled)> dispatch, bool captureContext)
{
    private readonly ExecutionContext _context = captureContext ? ExecutionContext.Capture() : null;
    private readonly TaskCompletionSource<Task> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _handled;

    internal Task Completion => _completion.Task.Unwrap();

    internal bool Dispatch()
    {
        if (_context == null) Invoke();
        else ExecutionContext.Run(_context, static state => ((IoCompletion)state).Invoke(), this);
        return _handled;
    }

    private void Invoke()
    {
        try
        {
            var result = dispatch();
            _handled = result.Handled;
            _completion.SetResult(result.Completion);
        }
        catch (Exception error) { _completion.SetException(error); }
    }
}
