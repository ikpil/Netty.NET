using System;
using System.Diagnostics;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Common.Tests.Internal.Logging;

namespace Netty.NET.Common.Tests.Porting;

// Exercise the upstream abstract fixture against the CLR backend. JVM-specific
// backend tests remain separate entries in the manifest.
public class TraceSourceLoggerInterfaceTest : AbstractInternalLoggerTest<TraceSource>, IDisposable
{
    public TraceSourceLoggerInterfaceTest()
    {
        mockLog = new TraceSource(loggerName, SourceLevels.All);
        mockLog.Listeners.Clear();
        mockLog.Listeners.Add(new CapturingListener((level, message) =>
        {
            result["level"] = level;
            result["message"] = message;
        }));
        logger = new InternalDefaultLogger(loggerName, mockLog);
    }

    protected override void setLevelEnable(InternalLogLevel level, bool enable)
        => mockLog.Switch.Level = enable ? SourceLevels.All : SourceLevels.Off;

    protected override void assertResult(InternalLogLevel level, string format, Exception cause, params object[] args)
    {
        base.assertResult(level, format, cause, args);
        TraceEventType expected = level switch
        {
            InternalLogLevel.TRACE or InternalLogLevel.DEBUG => TraceEventType.Verbose,
            InternalLogLevel.INFO => TraceEventType.Information,
            InternalLogLevel.WARN => TraceEventType.Warning,
            _ => TraceEventType.Error
        };
        Assert.Equal(expected, result["level"]);
        string message = format == null
            ? args.Length == 0 ? "Unexpected exception:" : (string)args[0]
            : MessageFormatter.arrayFormat(format, args).getMessage();
        if (cause != null) message += Environment.NewLine + cause;
        Assert.Equal(message, result["message"]);
    }

    public void Dispose() => mockLog.Close();

    private sealed class CapturingListener(Action<TraceEventType, string> capture) : TraceListener
    {
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
            => capture(eventType, message);
        public override void Write(string message) { }
        public override void WriteLine(string message) { }
    }
}
