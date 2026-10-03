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
        _source = ObjectUtil.CheckNotNull(source, nameof(source));
    }

    private void Write(TraceEventType level, string message, Exception cause = null)
    {
        if (!_source.Switch.ShouldTrace(level)) return;
        _source.TraceEvent(level, 0, cause == null ? message : message + Environment.NewLine + cause);
    }

    private void Formatted(TraceEventType level, string format, params object[] args)
    {
        if (!_source.Switch.ShouldTrace(level)) return;
        FormattingTuple tuple = MessageFormatter.ArrayFormat(format, args);
        Write(level, tuple.GetMessage(), tuple.GetThrowable());
    }

    public override bool IsTraceEnabled() => _source.Switch.ShouldTrace(TraceEventType.Verbose);
    public override void Trace(string msg) => Write(TraceEventType.Verbose, msg);
    public override void Trace(string msg, Exception cause) => Write(TraceEventType.Verbose, msg, cause);
    public override void Trace(string format, object arg) => Formatted(TraceEventType.Verbose, format, arg);
    public override void Trace(string format, object argA, object argB) => Formatted(TraceEventType.Verbose, format, argA, argB);
    public override void Trace(string format, params object[] args) => Formatted(TraceEventType.Verbose, format, args);

    public override bool IsDebugEnabled() => _source.Switch.ShouldTrace(TraceEventType.Verbose);
    public override void Debug(string msg) => Write(TraceEventType.Verbose, msg);
    public override void Debug(string msg, Exception cause) => Write(TraceEventType.Verbose, msg, cause);
    public override void Debug(string format, object arg) => Formatted(TraceEventType.Verbose, format, arg);
    public override void Debug(string format, object argA, object argB) => Formatted(TraceEventType.Verbose, format, argA, argB);
    public override void Debug(string format, params object[] args) => Formatted(TraceEventType.Verbose, format, args);

    public override bool IsInfoEnabled() => _source.Switch.ShouldTrace(TraceEventType.Information);
    public override void Info(string msg) => Write(TraceEventType.Information, msg);
    public override void Info(string msg, Exception cause) => Write(TraceEventType.Information, msg, cause);
    public override void Info(string format, object arg) => Formatted(TraceEventType.Information, format, arg);
    public override void Info(string format, object argA, object argB) => Formatted(TraceEventType.Information, format, argA, argB);
    public override void Info(string format, params object[] args) => Formatted(TraceEventType.Information, format, args);

    public override bool IsWarnEnabled() => _source.Switch.ShouldTrace(TraceEventType.Warning);
    public override void Warn(string msg) => Write(TraceEventType.Warning, msg);
    public override void Warn(string msg, Exception cause) => Write(TraceEventType.Warning, msg, cause);
    public override void Warn(string format, object arg) => Formatted(TraceEventType.Warning, format, arg);
    public override void Warn(string format, object argA, object argB) => Formatted(TraceEventType.Warning, format, argA, argB);
    public override void Warn(string format, params object[] args) => Formatted(TraceEventType.Warning, format, args);

    public override bool IsErrorEnabled() => _source.Switch.ShouldTrace(TraceEventType.Error);
    public override void Error(string msg) => Write(TraceEventType.Error, msg);
    public override void Error(string msg, Exception cause) => Write(TraceEventType.Error, msg, cause);
    public override void Error(string format, object arg) => Formatted(TraceEventType.Error, format, arg);
    public override void Error(string format, object argA, object argB) => Formatted(TraceEventType.Error, format, argA, argB);
    public override void Error(string format, params object[] args) => Formatted(TraceEventType.Error, format, args);
}
