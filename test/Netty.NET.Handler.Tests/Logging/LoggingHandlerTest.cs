/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Logging;

// CLR: @Isolated runs the class "in isolation": a collection with DisableParallelization is "guaranteed not to run in
// parallel against any other test" (https://xunit.net/docs/running-tests-in-parallel), which keeps the process-wide
// logger factory of the test to itself, as the Common InternalLoggerFactoryTest does with its collection.
[CollectionDefinition(nameof(LoggingHandlerTestCollection), DisableParallelization = true)]
public sealed class LoggingHandlerTestCollection;

/**
 * Verifies the correct functionality of the {@link LoggingHandler}.
 */
// CLR: Netty.NET logs through Common's InternalLoggerFactory instead of SLF4J/logback, so the logback Logger of
// LOGGER_NAME with its mocked Appender<ILoggingEvent> is a mocked IInternalLogger that a mocked
// IInternalLoggerFactory returns for LOGGER_NAME (other names get the previous factory's loggers), installed with
// InternalLoggerFactory.SetDefaultFactory as InternalLoggerFactoryTest in Common does. setup() installs it
// (logger.addAppender(appender)) and teardown() restores the previous factory (logger.detachAppender(appender)); a
// LoggingHandler reads its logger in its constructor, after setup(). One logging event is one
// IInternalLogger.Log(level, msg) call (Log(level, msg, cause) for exceptionCaught), so
// verify(appender).doAppend(argThat(matcher)) is Verify(Log(..., ArgThat(matcher)...), Times.Once()), where
// ArgThat is a Moq custom matcher built with Match.Create (https://github.com/devlooped/moq/wiki/Quickstart). The
// handler test class path has no logback-test.xml, so logback's default configuration applies, where "by default
// the root logger is assigned the DEBUG level" (https://logback.qos.ch/manual/configuration.html); the mocked
// logger reports the same enabled levels. beforeClass() detaches the root logger's console appender so that the
// class does not print its events, and afterClass() re-attaches it: the mocked logger prints nothing, so only
// Unpooled.buffer() remains and runs from the static constructor. InetSocketAddress(port) is the wildcard address
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html), IPAddress.Any,
// and it prints as IPEndPoint.ToString(), "0.0.0.0:80", not Java's "0.0.0.0/0.0.0.0:80"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0), so the expected
// texts of the connect and bind events use the CLR form. IllegalStateException is InvalidOperationException,
// NullPointerException from checkNotNull is ArgumentNullException and getCanonicalName() is Type.FullName.
// channel.config() is Config, write(msg, newPromise()) is WriteAsync(msg), close()/connect()/disconnect().await()
// are awaits of operations that succeed, and readInbound() is ReadInbound<T>(). The channel operations receive
// TestContext.Current.CancellationToken (xUnit1051).
[Collection(nameof(LoggingHandlerTestCollection))]
public class LoggingHandlerTest : IDisposable
{

    private static readonly string LOGGER_NAME = typeof(LoggingHandler).FullName;

    private IInternalLoggerFactory oldLoggerFactory;
    /**
     * Custom logback appender which gets used to match on log messages.
     */
    private Mock<IInternalLogger> appender;

    static LoggingHandlerTest()
    {
        BeforeClass();
    }

    private static void BeforeClass()
    {
        Unpooled.Buffer();
    }

    public LoggingHandlerTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        appender = new Mock<IInternalLogger>();
        appender.Setup(x => x.IsEnabled(It.IsAny<InternalLogLevel>()))
                .Returns((InternalLogLevel level) => level >= InternalLogLevel.DEBUG);
        oldLoggerFactory = InternalLoggerFactory.GetDefaultFactory();
        Mock<IInternalLoggerFactory> factory = new Mock<IInternalLoggerFactory>();
        factory.Setup(x => x.NewInstance(It.IsAny<string>()))
                .Returns((string name) => name == LOGGER_NAME ? appender.Object : oldLoggerFactory.NewInstance(name));
        InternalLoggerFactory.SetDefaultFactory(factory.Object);
    }

    private void Teardown()
    {
        InternalLoggerFactory.SetDefaultFactory(oldLoggerFactory);
    }

    [Fact]
    public void ShouldNotAcceptNullLogLevel()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            LogLevel level = null;
            new LoggingHandler(level);
        });
    }

    [Fact]
    public void ShouldApplyCustomLogLevel()
    {
        LoggingHandler handler = new LoggingHandler(LogLevel.INFO);
        Assert.Equal(LogLevel.INFO, handler.Level());
    }

    [Fact]
    public void ShouldLogChannelActive()
    {
        new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+ACTIVE$"))), Times.Once());
    }

    [Fact]
    public void ShouldLogChannelWritabilityChanged()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        // this is used to switch the channel to become unwritable
        channel.Config.SetWriteBufferLowWaterMark(5);
        channel.Config.SetWriteBufferHighWaterMark(10);
        _ = channel.WriteAsync("hello", TestContext.Current.CancellationToken);

        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+WRITABILITY CHANGED$"))),
                Times.Once());
    }

    [Fact]
    public void ShouldLogChannelRegistered()
    {
        new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+REGISTERED$"))),
                Times.Once());
    }

    [Fact]
    public async Task ShouldLogChannelClose()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        await channel.CloseAsync(TestContext.Current.CancellationToken);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+CLOSE$"))), Times.Once());
    }

    [Fact]
    public async Task ShouldLogChannelConnect()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        await channel.ConnectAsync(new IPEndPoint(IPAddress.Any, 80),
                cancellationToken: TestContext.Current.CancellationToken);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+CONNECT: 0.0.0.0:80$"))),
                Times.Once());
    }

    [Fact]
    public async Task ShouldLogChannelConnectWithLocalAddress()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        await channel.ConnectAsync(new IPEndPoint(IPAddress.Any, 80), new IPEndPoint(IPAddress.Any, 81),
                TestContext.Current.CancellationToken);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(
                "^\\[id: 0xembedded, L:embedded - R:embedded\\] CONNECT: 0.0.0.0:80, 0.0.0.0:81$"))), Times.Once());
    }

    [Fact]
    public async Task ShouldLogChannelDisconnect()
    {
        EmbeddedChannel channel = new DisconnectingEmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        await channel.ConnectAsync(new IPEndPoint(IPAddress.Any, 80),
                cancellationToken: TestContext.Current.CancellationToken);
        await channel.DisconnectAsync(TestContext.Current.CancellationToken);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+DISCONNECT$"))),
                Times.Once());
    }

    [Fact]
    public void ShouldLogChannelInactive()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.Pipeline.FireChannelInactive();
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+INACTIVE$"))),
                Times.Once());
    }

    [Fact]
    public void ShouldLogChannelBind()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        _ = channel.BindAsync(new IPEndPoint(IPAddress.Any, 80), TestContext.Current.CancellationToken);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+BIND: 0.0.0.0:80$"))),
                Times.Once());
    }

    [Fact]
    public void ShouldLogChannelUserEvent()
    {
        string userTriggered = "iAmCustom!";
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.Pipeline.FireUserEventTriggered(new string(userTriggered.AsSpan()));
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(),
                ArgThat(new RegexLogMatcher(".+USER_EVENT: " + userTriggered + '$'))), Times.Once());
    }

    [Fact]
    public void ShouldLogChannelException()
    {
        string msg = "illegalState";
        Exception cause = new InvalidOperationException(msg);
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.Pipeline.FireExceptionCaught(cause);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(
                ".+EXCEPTION: " + cause.GetType().FullName + ": " + msg + '$')), It.IsAny<Exception>()), Times.Once());
    }

    [Fact]
    public void ShouldLogDataWritten()
    {
        string msg = "hello";
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteOutbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+WRITE: " + msg + '$'))),
                Times.Once());
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+FLUSH$"))), Times.Once());
    }

    [Fact]
    public void ShouldLogNonByteBufDataRead()
    {
        string msg = "hello";
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+READ: " + msg + '$'))),
                Times.Once());

        string handledMsg = channel.ReadInbound<string>();
        Assert.Same(msg, handledMsg);
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void ShouldLogByteBufDataRead()
    {
        ByteBuf msg = Unpooled.CopiedBuffer("hello", Encoding.UTF8);
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(),
                ArgThat(new RegexLogMatcher(".+READ: " + msg.ReadableBytes + "B$", true))), Times.Once());

        ByteBuf handledMsg = channel.ReadInbound<ByteBuf>();
        Assert.Same(msg, handledMsg);
        handledMsg.Release();
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void ShouldLogByteBufDataReadWithSimpleFormat()
    {
        ByteBuf msg = Unpooled.CopiedBuffer("hello", Encoding.UTF8);
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN, ByteBufFormat.SIMPLE));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(),
                ArgThat(new RegexLogMatcher(".+READ: " + msg.ReadableBytes + "B$", false))), Times.Once());

        ByteBuf handledMsg = channel.ReadInbound<ByteBuf>();
        Assert.Same(msg, handledMsg);
        handledMsg.Release();
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void ShouldLogEmptyByteBufDataRead()
    {
        ByteBuf msg = Unpooled.EmptyBuffer;
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+READ: 0B$", false))),
                Times.Once());

        ByteBuf handledMsg = channel.ReadInbound<ByteBuf>();
        Assert.Same(msg, handledMsg);
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void ShouldLogByteBufHolderDataRead()
    {
        IByteBufHolder msg = new FoobarByteBufHolder(Unpooled.CopiedBuffer("hello", Encoding.UTF8));

        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(),
                ArgThat(new RegexLogMatcher(".+READ: foobar, 5B$", true))), Times.Once());

        IByteBufHolder handledMsg = channel.ReadInbound<IByteBufHolder>();
        Assert.Same(msg, handledMsg);
        handledMsg.Release();
        Assert.Null(channel.ReadInbound());
    }

    private sealed class FoobarByteBufHolder : DefaultByteBufHolder
    {
        internal FoobarByteBufHolder(ByteBuf data) : base(data)
        {
        }

        public override string ToString()
        {
            return "foobar";
        }
    }

    [Fact]
    public void ShouldLogChannelReadComplete()
    {
        ByteBuf msg = Unpooled.EmptyBuffer;
        EmbeddedChannel channel = new EmbeddedChannel(new LoggingHandler(LogLevel.WARN));
        channel.WriteInbound(msg);
        appender.Verify(x => x.Log(It.IsAny<InternalLogLevel>(), ArgThat(new RegexLogMatcher(".+READ COMPLETE$"))),
                Times.Once());
    }

    private static string ArgThat(RegexLogMatcher matcher)
    {
        return Moq.Match.Create<string>(matcher.Matches);
    }

    /**
     * A custom EasyMock matcher that matches on Logback messages.
     */
    // CLR: ArgumentMatcher<ILoggingEvent> matches the logged message, the ILoggingEvent.getMessage() of the source.
    // String.split("(?s)[\\r\\n]+")[0] is Regex.Split(...)[0], String.matches(regex) matches the whole string, so the
    // pattern is anchored with \A(?:...)\z (Regex.IsMatch finds a match anywhere), and NEWLINE is StringUtil.NEWLINE.
    private sealed class RegexLogMatcher
    {

        private readonly string expected;
        private readonly bool shouldContainNewline;
        private string actualMsg;

        internal RegexLogMatcher(string expected) : this(expected, false)
        {
        }

        internal RegexLogMatcher(string expected, bool shouldContainNewline)
        {
            this.expected = expected;
            this.shouldContainNewline = shouldContainNewline;
        }

        public bool Matches(string actual)
        {
            // Match only the first line to skip the validation of hex-dump format.
            actualMsg = Regex.Split(actual, "(?s)[\\r\\n]+")[0];
            if (Regex.IsMatch(actualMsg, "\\A(?:" + expected + ")\\z"))
            {
                // The presence of a newline implies a hex-dump was logged
                return actual.Contains(StringUtil.NEWLINE) == shouldContainNewline;
            }

            return false;
        }
    }

    // CLR: metadata() is the Metadata property, fixed by the AbstractChannel constructor, so the subclass passes
    // hasDisconnect = true to the EmbeddedChannel(bool hasDisconnect, params IChannelHandler[]) constructor, which
    // selects the same new ChannelMetadata(true).
    private sealed class DisconnectingEmbeddedChannel : EmbeddedChannel
    {

        internal DisconnectingEmbeddedChannel(params IChannelHandler[] handlers) : base(true, handlers)
        {
        }
    }
}
