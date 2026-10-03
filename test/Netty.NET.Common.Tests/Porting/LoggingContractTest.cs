using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Porting;

public class LoggingContractTest
{
    [Fact]
    public void TraceSourcePreservesFormattingThrowableAndLevelFiltering()
    {
        var writer = new StringWriter();
        var source = new TraceSource("contract", SourceLevels.Warning);
        source.Listeners.Clear();
        source.Listeners.Add(new TextWriterTraceListener(writer));
        var logger = new InternalDefaultLogger("contract", source);
        try
        {
            Assert.False(logger.IsDebugEnabled());
            Assert.True(logger.IsWarnEnabled());
            logger.Debug("ignored {}", new ThrowingString());
            Assert.Equal("", writer.ToString());
            var cause = new InvalidOperationException("failure");
            logger.Warn("value {}", new object[] { 42, cause });
            source.Flush();
            Assert.Contains("value 42", writer.ToString());
            Assert.Contains(cause.ToString(), writer.ToString());
        }
        finally { source.Close(); }
    }

    [Fact]
    public void PrimitiveFormattingPreservesJavaByteSignAndBooleanCaseAcrossCultures()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1.5 [true, false] [-128, -1]", MessageFormatter.ArrayFormat(
                "{} {} {}", new object[] { 1.5, new[] { true, false }, new byte[] { 128, 255 } }).GetMessage());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private sealed class ThrowingString
    {
        public override string ToString() => throw new InvalidOperationException("Must not format disabled events");
    }
}
