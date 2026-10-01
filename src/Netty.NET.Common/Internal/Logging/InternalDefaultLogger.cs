using System;
using System.Diagnostics;

namespace Netty.NET.Common.Internal.Logging;

/// <summary>CLR TraceSource backend for Netty's internal logging contract.</summary>
public class InternalDefaultLogger : AbstractInternalLogger
{
    private readonly TraceSource _source;

    public InternalDefaultLogger(string name) : this(name, new TraceSource(name, SourceLevels.Warning)) { }
    public InternalDefaultLogger(string name, TraceSource source) : base(name)
    {
        _source = ObjectUtil.checkNotNull(source, nameof(source));
    }

    private void write(TraceEventType level, string message, Exception cause = null)
    {
        if (!_source.Switch.ShouldTrace(level)) return;
        _source.TraceEvent(level, 0, cause == null ? message : message + Environment.NewLine + cause);
    }

    private void formatted(TraceEventType level, string format, params object[] args)
    {
        if (!_source.Switch.ShouldTrace(level)) return;
        FormattingTuple tuple = MessageFormatter.arrayFormat(format, args);
        write(level, tuple.getMessage(), tuple.getThrowable());
    }

    public override bool isTraceEnabled() => _source.Switch.ShouldTrace(TraceEventType.Verbose);
    public override void trace(string msg) => write(TraceEventType.Verbose, msg);
    public override void trace(string msg, Exception cause) => write(TraceEventType.Verbose, msg, cause);
    public override void trace(string format, object arg) => formatted(TraceEventType.Verbose, format, arg);
    public override void trace(string format, object argA, object argB) => formatted(TraceEventType.Verbose, format, argA, argB);
    public override void trace(string format, params object[] args) => formatted(TraceEventType.Verbose, format, args);

    public override bool isDebugEnabled() => _source.Switch.ShouldTrace(TraceEventType.Verbose);
    public override void debug(string msg) => write(TraceEventType.Verbose, msg);
    public override void debug(string msg, Exception cause) => write(TraceEventType.Verbose, msg, cause);
    public override void debug(string format, object arg) => formatted(TraceEventType.Verbose, format, arg);
    public override void debug(string format, object argA, object argB) => formatted(TraceEventType.Verbose, format, argA, argB);
    public override void debug(string format, params object[] args) => formatted(TraceEventType.Verbose, format, args);

    public override bool isInfoEnabled() => _source.Switch.ShouldTrace(TraceEventType.Information);
    public override void info(string msg) => write(TraceEventType.Information, msg);
    public override void info(string msg, Exception cause) => write(TraceEventType.Information, msg, cause);
    public override void info(string format, object arg) => formatted(TraceEventType.Information, format, arg);
    public override void info(string format, object argA, object argB) => formatted(TraceEventType.Information, format, argA, argB);
    public override void info(string format, params object[] args) => formatted(TraceEventType.Information, format, args);

    public override bool isWarnEnabled() => _source.Switch.ShouldTrace(TraceEventType.Warning);
    public override void warn(string msg) => write(TraceEventType.Warning, msg);
    public override void warn(string msg, Exception cause) => write(TraceEventType.Warning, msg, cause);
    public override void warn(string format, object arg) => formatted(TraceEventType.Warning, format, arg);
    public override void warn(string format, object argA, object argB) => formatted(TraceEventType.Warning, format, argA, argB);
    public override void warn(string format, params object[] args) => formatted(TraceEventType.Warning, format, args);

    public override bool isErrorEnabled() => _source.Switch.ShouldTrace(TraceEventType.Error);
    public override void error(string msg) => write(TraceEventType.Error, msg);
    public override void error(string msg, Exception cause) => write(TraceEventType.Error, msg, cause);
    public override void error(string format, object arg) => formatted(TraceEventType.Error, format, arg);
    public override void error(string format, object argA, object argB) => formatted(TraceEventType.Error, format, argA, argB);
    public override void error(string format, params object[] args) => formatted(TraceEventType.Error, format, args);
}
