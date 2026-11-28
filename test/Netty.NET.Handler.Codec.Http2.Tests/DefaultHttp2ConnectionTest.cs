/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2Connection}.
 */
// CLR: the static EventLoopGroup of @BeforeAll/@AfterAll only creates the promises of close(Promise); a promise is a
// TaskCompletionSource (Http2CodecUtil.NewPromise; docs/codec-http-porting.md, Shared CLR adaptations), so the group
// and its beforeClass/afterClass have no counterpart, and promise.addListener(...) is a continuation of the returned
// Task (future.isDone() is IsCompleted). CountDownLatch is CountdownEvent (docs/codec-http-porting.md, Shared CLR
// adaptations). Mockito's @Mock listeners are Moq mocks created in the constructor (@BeforeEach; xUnit creates a test
// class instance per test, https://xunit.net/docs/shared-context); doAnswer(answer).when(listener).onX(...) is a Moq
// Setup with a Callback (the Answer classes keep an Answer() method that the callback calls), verify(...) is
// Verify(..., Times.Once()) and never() is Times.Never() (Moq Quickstart, https://github.com/devlooped/moq/wiki/Quickstart).
// streamEq is a Moq custom matcher (Match.Create) that matches null or a stream equal to the argument, as
// ArgumentMatchers.isNull()/eq(stream) do. assertThrows(T.class, ...) is Assert.ThrowsAny<T>
// (docs/codec-http-porting.md, Core test notes) and the anonymous Executables and Http2StreamVisitors are lambdas or
// private nested classes. Endpoint<?> is the non-generic IHttp2Connection.IEndpoint. MAX_VALUE + 1/+ 2 overflow as in
// Java (unchecked). fail(Throwable) is Assert.Fail with the exception's text. The generic getProperty/removeProperty
// calls name their type argument (string), which Java infers. @Nonnull and @SuppressWarnings have no counterpart.
public class DefaultHttp2ConnectionTest
{

    private DefaultHttp2Connection server;
    private DefaultHttp2Connection client;

    private readonly Mock<IHttp2Connection.IListener> clientListener = new Mock<IHttp2Connection.IListener>();

    private readonly Mock<IHttp2Connection.IListener> clientListener2 = new Mock<IHttp2Connection.IListener>();

    public DefaultHttp2ConnectionTest()
    {
        server = new DefaultHttp2Connection(true);
        client = new DefaultHttp2Connection(false);
        client.AddListener(clientListener.Object);
        clientListener.Setup(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>()))
            .Callback<IHttp2Stream>(stream => Assert.NotNull(client.Stream(stream.Id())));
        clientListener.Setup(l => l.OnStreamRemoved(It.IsAny<IHttp2Stream>()))
            .Callback<IHttp2Stream>(stream => Assert.Null(client.Stream(stream.Id())));
    }

    [Fact]
    public void GetStreamWithoutStreamShouldReturnNull()
    {
        Assert.Null(server.Stream(100));
    }

    [Fact]
    public void RemoveAllStreamsWithEmptyStreams()
    {
        TestRemoveAllStreams();
    }

    [Fact]
    public void RemoveAllStreamsWithJustOneLocalStream()
    {
        client.Local().CreateStream(3, false);
        TestRemoveAllStreams();
    }

    [Fact]
    public void RemoveAllStreamsWithJustOneRemoveStream()
    {
        client.Remote().CreateStream(2, false);
        TestRemoveAllStreams();
    }

    [Fact]
    public void RemoveAllStreamsWithManyActiveStreams()
    {
        IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote = client.Remote();
        IHttp2Connection.IEndpoint<IHttp2LocalFlowController> local = client.Local();

        for (int c = 3, s = 2; c < 5000; c += 2, s += 2)
        {
            local.CreateStream(c, false);
            remote.CreateStream(s, false);
        }

        TestRemoveAllStreams();
    }

    [Fact]
    public void RemoveIndividualStreamsWhileCloseDoesNotNPE()
    {
        IHttp2Stream streamA = client.Local().CreateStream(3, false);
        IHttp2Stream streamB = client.Remote().CreateStream(2, false);
        clientListener2.Setup(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>())).Callback(() =>
        {
            streamA.Close();
            streamB.Close();
        });

        try
        {
            client.AddListener(clientListener2.Object);
            TestRemoveAllStreams();
        }
        finally
        {
            client.RemoveListener(clientListener2.Object);
        }
    }

    [Fact]
    public void RemoveAllStreamsWhileIteratingActiveStreams()
    {
        IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote = client.Remote();
        IHttp2Connection.IEndpoint<IHttp2LocalFlowController> local = client.Local();

        for (int c = 3, s = 2; c < 5000; c += 2, s += 2)
        {
            local.CreateStream(c, false);
            remote.CreateStream(s, false);
        }

        TaskCompletionSource promise = new TaskCompletionSource();
        using CountdownEvent latch = new CountdownEvent(client.NumActiveStreams());
        client.ForEachActiveStream(new CloseVisitor(client, promise, latch));
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // CLR: the anonymous Http2StreamVisitor of removeAllStreamsWhileIteratingActiveStreams.
    private sealed class CloseVisitor : IHttp2StreamVisitor
    {
        private readonly DefaultHttp2Connection client;
        private readonly TaskCompletionSource promise;
        private readonly CountdownEvent latch;

        internal CloseVisitor(DefaultHttp2Connection client, TaskCompletionSource promise, CountdownEvent latch)
        {
            this.client = client;
            this.promise = promise;
            this.latch = latch;
        }

        public bool Visit(IHttp2Stream stream)
        {
            _ = client.CloseAsync(promise).ContinueWith(future =>
            {
                Assert.True(promise.Task.IsCompleted);
                latch.Signal();
            }, TestContext.Current.CancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
            return true;
        }
    }

    [Fact]
    public void RemoveAllStreamsWhileIteratingActiveStreamsAndExceptionOccurs()
    {
        IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote = client.Remote();
        IHttp2Connection.IEndpoint<IHttp2LocalFlowController> local = client.Local();

        for (int c = 3, s = 2; c < 5000; c += 2, s += 2)
        {
            local.CreateStream(c, false);
            remote.CreateStream(s, false);
        }

        TaskCompletionSource promise = new TaskCompletionSource();
        using CountdownEvent latch = new CountdownEvent(1);

        try
        {
            client.ForEachActiveStream(new CloseAndThrowVisitor(client, remote, promise));
        }
        catch (Http2Exception)
        {
            _ = client.CloseAsync(promise).ContinueWith(future =>
            {
                Assert.True(promise.Task.IsCompleted);
                latch.Signal();
            }, TestContext.Current.CancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        Assert.True(latch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // CLR: the anonymous Http2StreamVisitor of removeAllStreamsWhileIteratingActiveStreamsAndExceptionOccurs.
    private sealed class CloseAndThrowVisitor : IHttp2StreamVisitor
    {
        private readonly DefaultHttp2Connection client;
        private readonly IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote;
        private readonly TaskCompletionSource promise;

        internal CloseAndThrowVisitor(DefaultHttp2Connection client,
                                      IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote,
                                      TaskCompletionSource promise)
        {
            this.client = client;
            this.remote = remote;
            this.promise = promise;
        }

        public bool Visit(IHttp2Stream stream)
        {
            // This close call is basically a noop, because the following statement will throw an exception.
            client.CloseAsync(promise);
            // Do an invalid operation while iterating.
            remote.CreateStream(3, false);
            return true;
        }
    }

    [Fact]
    public void GoAwayReceivedShouldCloseStreamsGreaterThanLastStream()
    {
        IHttp2Stream stream1 = client.Local().CreateStream(3, false);
        IHttp2Stream stream2 = client.Local().CreateStream(5, false);
        IHttp2Stream remoteStream = client.Remote().CreateStream(4, false);

        Assert.Equal(State.OPEN, stream1.State_());
        Assert.Equal(State.OPEN, stream2.State_());

        client.GoAwayReceived(3, 8, null);

        Assert.Equal(State.OPEN, stream1.State_());
        Assert.Equal(State.CLOSED, stream2.State_());
        Assert.Equal(State.OPEN, remoteStream.State_());
        Assert.Equal(3, client.Local().LastStreamKnownByPeer());
        Assert.Equal(5, client.Local().LastStreamCreated());
        // The remote endpoint must not be affected by a received GOAWAY frame.
        Assert.Equal(-1, client.Remote().LastStreamKnownByPeer());
        Assert.Equal(State.OPEN, remoteStream.State_());
    }

    [Fact]
    public void GoAwaySentShouldCloseStreamsGreaterThanLastStream()
    {
        IHttp2Stream stream1 = server.Remote().CreateStream(3, false);
        IHttp2Stream stream2 = server.Remote().CreateStream(5, false);
        IHttp2Stream localStream = server.Local().CreateStream(4, false);

        server.GoAwaySent(3, 8, null);

        Assert.Equal(State.OPEN, stream1.State_());
        Assert.Equal(State.CLOSED, stream2.State_());

        Assert.Equal(3, server.Remote().LastStreamKnownByPeer());
        Assert.Equal(5, server.Remote().LastStreamCreated());
        // The local endpoint must not be affected by a sent GOAWAY frame.
        Assert.Equal(-1, server.Local().LastStreamKnownByPeer());
        Assert.Equal(State.OPEN, localStream.State_());
    }

    [Fact]
    public void ServerCreateStreamShouldSucceed()
    {
        IHttp2Stream stream = server.Local().CreateStream(2, false);
        Assert.Equal(2, stream.Id());
        Assert.Equal(State.OPEN, stream.State_());
        Assert.Equal(1, server.NumActiveStreams());
        Assert.Equal(2, server.Local().LastStreamCreated());

        stream = server.Local().CreateStream(4, true);
        Assert.Equal(4, stream.Id());
        Assert.Equal(State.HALF_CLOSED_LOCAL, stream.State_());
        Assert.Equal(2, server.NumActiveStreams());
        Assert.Equal(4, server.Local().LastStreamCreated());

        stream = server.Remote().CreateStream(3, true);
        Assert.Equal(3, stream.Id());
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());
        Assert.Equal(3, server.NumActiveStreams());
        Assert.Equal(3, server.Remote().LastStreamCreated());

        stream = server.Remote().CreateStream(5, false);
        Assert.Equal(5, stream.Id());
        Assert.Equal(State.OPEN, stream.State_());
        Assert.Equal(4, server.NumActiveStreams());
        Assert.Equal(5, server.Remote().LastStreamCreated());
    }

    [Fact]
    public void ClientCreateStreamShouldSucceed()
    {
        IHttp2Stream stream = client.Remote().CreateStream(2, false);
        Assert.Equal(2, stream.Id());
        Assert.Equal(State.OPEN, stream.State_());
        Assert.Equal(1, client.NumActiveStreams());
        Assert.Equal(2, client.Remote().LastStreamCreated());

        stream = client.Remote().CreateStream(4, true);
        Assert.Equal(4, stream.Id());
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());
        Assert.Equal(2, client.NumActiveStreams());
        Assert.Equal(4, client.Remote().LastStreamCreated());
        Assert.True(stream.IsHeadersReceived());

        stream = client.Local().CreateStream(3, true);
        Assert.Equal(3, stream.Id());
        Assert.Equal(State.HALF_CLOSED_LOCAL, stream.State_());
        Assert.Equal(3, client.NumActiveStreams());
        Assert.Equal(3, client.Local().LastStreamCreated());
        Assert.True(stream.IsHeadersSent());

        stream = client.Local().CreateStream(5, false);
        Assert.Equal(5, stream.Id());
        Assert.Equal(State.OPEN, stream.State_());
        Assert.Equal(4, client.NumActiveStreams());
        Assert.Equal(5, client.Local().LastStreamCreated());
    }

    [Fact]
    public void ServerReservePushStreamShouldSucceed()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, true);
        IHttp2Stream pushStream = server.Local().ReservePushStream(2, stream);
        Assert.Equal(2, pushStream.Id());
        Assert.Equal(State.RESERVED_LOCAL, pushStream.State_());
        Assert.Equal(1, server.NumActiveStreams());
        Assert.Equal(2, server.Local().LastStreamCreated());
    }

    [Fact]
    public void ClientReservePushStreamShouldSucceed()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, true);
        IHttp2Stream pushStream = server.Local().ReservePushStream(4, stream);
        Assert.Equal(4, pushStream.Id());
        Assert.Equal(State.RESERVED_LOCAL, pushStream.State_());
        Assert.Equal(1, server.NumActiveStreams());
        Assert.Equal(4, server.Local().LastStreamCreated());
    }

    [Fact]
    public void ServerRemoteIncrementAndGetStreamShouldSucceed()
    {
        IncrementAndGetStreamShouldSucceed(server.Remote());
    }

    [Fact]
    public void ServerLocalIncrementAndGetStreamShouldSucceed()
    {
        IncrementAndGetStreamShouldSucceed(server.Local());
    }

    [Fact]
    public void ClientRemoteIncrementAndGetStreamShouldSucceed()
    {
        IncrementAndGetStreamShouldSucceed(client.Remote());
    }

    [Fact]
    public void ClientLocalIncrementAndGetStreamShouldSucceed()
    {
        IncrementAndGetStreamShouldSucceed(client.Local());
    }

    [Fact]
    public void ServerRemoteIncrementAndGetStreamShouldRespectOverflow()
    {
        IncrementAndGetStreamShouldRespectOverflow(server.Remote(), int.MaxValue);
    }

    [Fact]
    public void ServerLocalIncrementAndGetStreamShouldRespectOverflow()
    {
        IncrementAndGetStreamShouldRespectOverflow(server.Local(), int.MaxValue - 1);
    }

    [Fact]
    public void ClientRemoteIncrementAndGetStreamShouldRespectOverflow()
    {
        IncrementAndGetStreamShouldRespectOverflow(client.Remote(), int.MaxValue - 1);
    }

    [Fact]
    public void ClientLocalIncrementAndGetStreamShouldRespectOverflow()
    {
        IncrementAndGetStreamShouldRespectOverflow(client.Local(), int.MaxValue);
    }

    [Fact]
    public void ClientLocalCreateStreamExhaustedSpace()
    {
        client.Local().CreateStream(int.MaxValue, true);
        Http2Exception expected = Assert.ThrowsAny<Http2Exception>(() =>
            {
                client.Local().CreateStream(int.MaxValue, true);
            });
        Assert.Equal(Http2Error.REFUSED_STREAM, expected.Error());
        Assert.Equal(Http2Exception.ShutdownHint.GRACEFUL_SHUTDOWN, expected.ShutdownHint_());
    }

    [Fact]
    public void NewStreamBehindExpectedShouldThrow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Local().CreateStream(0, true);
            });
    }

    [Fact]
    public void NewStreamNotForServerShouldThrow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Local().CreateStream(11, true);
            });
    }

    [Fact]
    public void NewStreamNotForClientShouldThrow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                client.Local().CreateStream(10, true);
            });
    }

    [Fact]
    public void CreateShouldThrowWhenMaxAllowedStreamsOpenExceeded()
    {
        server.Local().MaxActiveStreams(0);
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Local().CreateStream(2, true);
            });
    }

    [Fact]
    public void ServerCreatePushShouldFailOnRemoteEndpointWhenMaxAllowedStreamsExceeded()
    {
        server = new DefaultHttp2Connection(true, 0);
        server.Remote().MaxActiveStreams(1);
        IHttp2Stream requestStream = server.Remote().CreateStream(3, false);
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Remote().ReservePushStream(2, requestStream);
            });
    }

    [Fact]
    public void ClientCreatePushShouldFailOnRemoteEndpointWhenMaxAllowedStreamsExceeded()
    {
        client = new DefaultHttp2Connection(false, 0);
        client.Remote().MaxActiveStreams(1);
        IHttp2Stream requestStream = client.Remote().CreateStream(2, false);
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                client.Remote().ReservePushStream(4, requestStream);
            });
    }

    [Fact]
    public void ServerCreatePushShouldSucceedOnLocalEndpointWhenMaxAllowedStreamsExceeded()
    {
        server = new DefaultHttp2Connection(true, 0);
        server.Local().MaxActiveStreams(1);
        IHttp2Stream requestStream = server.Remote().CreateStream(3, false);
        Assert.NotNull(server.Local().ReservePushStream(2, requestStream));
    }

    [Fact]
    public void ReserveWithPushDisallowedShouldThrow()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, true);
        server.Remote().AllowPushTo(false);
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Local().ReservePushStream(2, stream);
            });
    }

    [Fact]
    public void GoAwayReceivedShouldDisallowLocalCreation()
    {
        server.GoAwayReceived(0, 1L, Unpooled.EmptyBuffer);
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Local().CreateStream(3, true);
            });
    }

    [Fact]
    public void GoAwayReceivedShouldAllowRemoteCreation()
    {
        server.GoAwayReceived(0, 1L, Unpooled.EmptyBuffer);
        server.Remote().CreateStream(3, true);
    }

    [Fact]
    public void GoAwaySentShouldDisallowRemoteCreation()
    {
        server.GoAwaySent(0, 1L, Unpooled.EmptyBuffer);

        Assert.ThrowsAny<Http2Exception>(() =>
            {
                server.Remote().CreateStream(2, true);
            });
    }

    [Fact]
    public void GoAwaySentShouldAllowLocalCreation()
    {
        server.GoAwaySent(0, 1L, Unpooled.EmptyBuffer);
        server.Local().CreateStream(2, true);
    }

    [Fact]
    public void CloseShouldSucceed()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, true);
        stream.Close();
        Assert.Equal(State.CLOSED, stream.State_());
        Assert.Equal(0, server.NumActiveStreams());
    }

    [Fact]
    public void CloseLocalWhenOpenShouldSucceed()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, false);
        stream.CloseLocalSide();
        Assert.Equal(State.HALF_CLOSED_LOCAL, stream.State_());
        Assert.Equal(1, server.NumActiveStreams());
    }

    [Fact]
    public void CloseRemoteWhenOpenShouldSucceed()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, false);
        stream.CloseRemoteSide();
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());
        Assert.Equal(1, server.NumActiveStreams());
    }

    [Fact]
    public void CloseOnlyOpenSideShouldClose()
    {
        IHttp2Stream stream = server.Remote().CreateStream(3, true);
        stream.CloseLocalSide();
        Assert.Equal(State.CLOSED, stream.State_());
        Assert.Equal(0, server.NumActiveStreams());
    }

    [Fact]
    public void LocalStreamInvalidStreamIdShouldThrow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                client.Local().CreateStream(unchecked(int.MaxValue + 2), false);
            });
    }

    [Fact]
    public void RemoteStreamInvalidStreamIdShouldThrow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
            {
                client.Remote().CreateStream(unchecked(int.MaxValue + 1), false);
            });
    }

    /**
     * We force {@link #clientListener} methods to all throw a {@link RuntimeException} and verify the following:
     * <ol>
     * <li>all listener methods are called for both {@link #clientListener} and {@link #clientListener2}</li>
     * <li>{@link #clientListener2} is notified after {@link #clientListener}</li>
     * <li>{@link #clientListener2} methods are all still called despite {@link #clientListener}'s
     * method throwing a {@link RuntimeException}</li>
     * </ol>
     */
    [Fact]
    public void ListenerThrowShouldNotPreventOtherListenersFromBeingNotified()
    {
        bool[] calledArray = new bool[128];
        // The following setup will ensure that clientListener throws exceptions, and marks a value in an array
        // such that clientListener2 will verify that is set or fail the test.
        int methodIndex = 0;
        clientListener.Setup(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnStreamActive(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamActive(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnStreamHalfClosed(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamHalfClosed(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnStreamRemoved(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamRemoved(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnGoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnGoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnGoAwayReceived(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnGoAwayReceived(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        clientListener.Setup(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerExceptionThrower(calledArray, methodIndex).Answer);
        clientListener2.Setup(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()))
            .Callback(new ListenerVerifyCallAnswer(calledArray, methodIndex++).Answer);

        // Now we add clientListener2 and exercise all listener functionality
        try
        {
            client.AddListener(clientListener2.Object);
            IHttp2Stream stream = client.Local().CreateStream(3, false);
            clientListener.Verify(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener2.Verify(l => l.OnStreamAdded(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener.Verify(l => l.OnStreamActive(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener2.Verify(l => l.OnStreamActive(It.IsAny<IHttp2Stream>()), Times.Once());

            IHttp2Stream reservedStream = client.Remote().ReservePushStream(2, stream);
            clientListener.Verify(l => l.OnStreamActive(StreamEq(reservedStream)), Times.Never());
            clientListener2.Verify(l => l.OnStreamActive(StreamEq(reservedStream)), Times.Never());

            reservedStream.Open(false);
            clientListener.Verify(l => l.OnStreamActive(StreamEq(reservedStream)), Times.Once());
            clientListener2.Verify(l => l.OnStreamActive(StreamEq(reservedStream)), Times.Once());

            stream.CloseLocalSide();
            clientListener.Verify(l => l.OnStreamHalfClosed(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener2.Verify(l => l.OnStreamHalfClosed(It.IsAny<IHttp2Stream>()), Times.Once());

            stream.Close();
            clientListener.Verify(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener2.Verify(l => l.OnStreamClosed(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener.Verify(l => l.OnStreamRemoved(It.IsAny<IHttp2Stream>()), Times.Once());
            clientListener2.Verify(l => l.OnStreamRemoved(It.IsAny<IHttp2Stream>()), Times.Once());

            client.GoAwaySent(client.ConnectionStream().Id(), Http2Error.INTERNAL_ERROR.Code(), Unpooled.EmptyBuffer);
            clientListener.Verify(l => l.OnGoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Once());
            clientListener2.Verify(l => l.OnGoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Once());

            client.GoAwayReceived(client.ConnectionStream().Id(),
                    Http2Error.INTERNAL_ERROR.Code(), Unpooled.EmptyBuffer);
            clientListener.Verify(l => l.OnGoAwayReceived(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Once());
            clientListener2.Verify(l => l.OnGoAwayReceived(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Once());
        }
        finally
        {
            client.RemoveListener(clientListener2.Object);
        }
    }

    [Fact]
    public void ClientLastStreamCreatedWithoutStreamCreated()
    {
        Assert.Equal(0, client.Local().LastStreamCreated());
    }

    [Fact]
    public void ServerLastStreamCreatedWithoutStreamCreated()
    {
        Assert.Equal(0, server.Local().LastStreamCreated());
    }

    [Fact]
    public void ClientCreateMaxStreamId()
    {
        int id = int.MaxValue;
        client.Local().CreateStream(id, false);
        Assert.True(client.StreamMayHaveExisted(id));
        Assert.Equal(id, client.Local().LastStreamCreated());
    }

    [Fact]
    public void ServerCreateMaxStreamId()
    {
        int id = int.MaxValue - 1;
        server.Local().CreateStream(id, false);
        Assert.True(server.StreamMayHaveExisted(id));
        Assert.Equal(id, server.Local().LastStreamCreated());
    }

    private void TestRemoveAllStreams()
    {
        using CountdownEvent latch = new CountdownEvent(1);
        TaskCompletionSource promise = new TaskCompletionSource();
        _ = client.CloseAsync(promise).ContinueWith(future =>
        {
            Assert.True(promise.Task.IsCompleted);
            latch.Signal();
        }, TestContext.Current.CancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private static void IncrementAndGetStreamShouldRespectOverflow(IHttp2Connection.IEndpoint endpoint, int streamId)
    {
        Assert.True(streamId > 0);

        try
        {
            endpoint.CreateStream(streamId, true);
            streamId = endpoint.IncrementAndGetNextStreamId();
        }
        catch (Exception t)
        {
            Assert.Fail(t.ToString());
        }

        Assert.True(streamId < 0);
        int finalStreamId = streamId;
        Assert.ThrowsAny<Http2NoMoreStreamIdsException>(() =>
            {
                endpoint.CreateStream(finalStreamId, true);
            });
    }

    [Fact]
    public void DefaultSettingsShouldEnforceMaxConcurrentStreamsOnRemoteEndpoint()
    {
        // Build a server handler using default settings (no explicit maxConcurrentStreams override).
        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Http2FrameAdapter())
                .Build();

        EmbeddedChannel channel = new EmbeddedChannel(handler);

        try
        {
            // Feed the client connection preface.
            Assert.False(channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf()));

            ByteBuf clientSettings = ClientSettingsWithoutMaxConcurrentStreams();
            Assert.False(channel.WriteInbound(clientSettings));

            IHttp2Connection connection = handler.Connection();

            // The server's default (SMALLEST_MAX_CONCURRENT_STREAMS = 100) must be
            // enforced on the remote endpoint — the one that tracks client-initiated streams.
            Assert.Equal(Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS,
                    connection.Remote().MaxActiveStreams());

            // Create exactly the maximum allowed client-initiated (odd-numbered) streams.
            for (int id = 1; id < Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS * 2; id += 2)
            {
                connection.Remote().CreateStream(id, true);
            }

            Assert.Equal(Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS,
                    connection.NumActiveStreams());

            // The next stream must be refused.
            int nextStreamId = Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS * 2 + 1;
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
                {
                    handler.Connection().Remote().CreateStream(nextStreamId, true);
                });
            Assert.Equal(Http2Error.REFUSED_STREAM, e.Error());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void CustomMaxConcurrentStreamsShouldBeEnforcedOnRemoteEndpoint()
    {
        int maxConcurrentStreams = 150;

        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Http2FrameAdapter())
                .InitialSettings(new Http2Settings().MaxConcurrentStreams(maxConcurrentStreams))
                .Build();

        EmbeddedChannel channel = new EmbeddedChannel(handler);

        try
        {
            Assert.False(channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf()));

            ByteBuf clientSettings = ClientSettingsWithoutMaxConcurrentStreams();
            Assert.False(channel.WriteInbound(clientSettings));

            IHttp2Connection connection = handler.Connection();

            Assert.Equal(maxConcurrentStreams, connection.Remote().MaxActiveStreams());

            // Create exactly the configured limit of client-initiated streams.
            for (int id = 1; id < maxConcurrentStreams * 2; id += 2)
            {
                connection.Remote().CreateStream(id, true);
            }

            Assert.Equal(maxConcurrentStreams, connection.NumActiveStreams());

            // The next stream must be refused.
            int nextStreamId = maxConcurrentStreams * 2 + 1;
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
                {
                    handler.Connection().Remote().CreateStream(nextStreamId, true);
                });
            Assert.Equal(Http2Error.REFUSED_STREAM, e.Error());
        }
        finally
        {
           channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void DefaultSettingsShouldEnforceMaxConcurrentStreamsOnRemoteEndpointWithCodec()
    {
        DefaultHttp2Connection connection = new DefaultHttp2Connection(true);
        DefaultHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
        IHttp2ConnectionEncoder encoder = new DefaultHttp2ConnectionEncoder(connection, frameWriter);
        IHttp2ConnectionDecoder decoder =
                new DefaultHttp2ConnectionDecoder(connection, encoder, new DefaultHttp2FrameReader());

        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Http2FrameAdapter())
                .Codec(decoder, encoder)
                .Build();

        EmbeddedChannel channel = new EmbeddedChannel(handler);

        try
        {
            Assert.False(channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf()));
            Assert.False(channel.WriteInbound(ClientSettingsWithoutMaxConcurrentStreams()));

            Assert.Equal(Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS,
                    connection.Remote().MaxActiveStreams());

            for (int id = 1; id < Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS * 2; id += 2)
            {
                connection.Remote().CreateStream(id, true);
            }

            Assert.Equal(Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS, connection.NumActiveStreams());

            int nextStreamId = Http2CodecUtil.SMALLEST_MAX_CONCURRENT_STREAMS * 2 + 1;
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
                {
                    connection.Remote().CreateStream(nextStreamId, true);
                });
            Assert.Equal(Http2Error.REFUSED_STREAM, e.Error());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void CustomMaxConcurrentStreamsShouldBeEnforcedOnRemoteEndpointWithCodec()
    {
        int maxConcurrentStreams = 150;

        DefaultHttp2Connection connection = new DefaultHttp2Connection(true);
        DefaultHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
        IHttp2ConnectionEncoder encoder = new DefaultHttp2ConnectionEncoder(connection, frameWriter);
        IHttp2ConnectionDecoder decoder =
                new DefaultHttp2ConnectionDecoder(connection, encoder, new DefaultHttp2FrameReader());

        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Http2FrameAdapter())
                .InitialSettings(new Http2Settings().MaxConcurrentStreams(maxConcurrentStreams))
                .Codec(decoder, encoder)
                .Build();

        EmbeddedChannel channel = new EmbeddedChannel(handler);

        try
        {
            Assert.False(channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf()));
            Assert.False(channel.WriteInbound(ClientSettingsWithoutMaxConcurrentStreams()));

            Assert.Equal(maxConcurrentStreams, connection.Remote().MaxActiveStreams());

            for (int id = 1; id < maxConcurrentStreams * 2; id += 2)
            {
                connection.Remote().CreateStream(id, true);
            }

            Assert.Equal(maxConcurrentStreams, connection.NumActiveStreams());

            int nextStreamId = maxConcurrentStreams * 2 + 1;
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
                {
                    connection.Remote().CreateStream(nextStreamId, true);
                });
            Assert.Equal(Http2Error.REFUSED_STREAM, e.Error());
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private static ByteBuf ClientSettingsWithoutMaxConcurrentStreams()
    {
        ByteBuf clientSettings = Unpooled.Buffer();
        clientSettings.WriteMedium(6);       // Payload length: one 6-byte setting
        clientSettings.WriteByte(0x4);       // Frame type: SETTINGS
        clientSettings.WriteByte(0x0);       // Flags
        clientSettings.WriteInt(0x0);        // Stream 0
        clientSettings.WriteShort(0x4);      // SETTINGS_INITIAL_WINDOW_SIZE
        clientSettings.WriteInt(65535);
        return clientSettings;
    }

    private static void IncrementAndGetStreamShouldSucceed(IHttp2Connection.IEndpoint endpoint)
    {
        IHttp2Stream streamA = endpoint.CreateStream(endpoint.IncrementAndGetNextStreamId(), true);
        IHttp2Stream streamB = endpoint.CreateStream(streamA.Id() + 2, true);
        IHttp2Stream streamC = endpoint.CreateStream(endpoint.IncrementAndGetNextStreamId(), true);
        Assert.Equal(streamB.Id() + 2, streamC.Id());
        endpoint.CreateStream(streamC.Id() + 2, true);
    }

    [Fact]
    public void ConnectionKeyShouldRejectUseWithOtherConnection()
    {
        IHttp2Connection.IPropertyKey key = client.NewKey();
        IHttp2Stream stream = server.Local().CreateStream(2, false);
        Assert.ThrowsAny<ArgumentException>(() => stream.SetProperty(key, "foo"));
    }

    [Fact]
    public void ConnectionKeyShouldWorkAcrossStreamsOfSameConnection()
    {
        IHttp2Connection.IPropertyKey key = client.NewKey();
        IHttp2Stream streamA = client.Local().CreateStream(3, false);
        IHttp2Stream streamB = client.Local().CreateStream(5, false);
        Assert.Null(streamA.SetProperty(key, "a"));
        Assert.Null(streamB.SetProperty(key, "b"));
        Assert.Equal("a", streamA.GetProperty<string>(key));
        Assert.Equal("b", streamB.GetProperty<string>(key));
        Assert.Equal("a", streamA.RemoveProperty<string>(key));
        Assert.Null(streamA.GetProperty<string>(key));
        Assert.Equal("b", streamB.GetProperty<string>(key));
    }

    [Fact]
    public void GlobalKeyShouldWorkWithAnyConnection()
    {
        IHttp2Connection.IPropertyKey key = Http2ConnectionPropertyKeys.NewGlobalKey();
        IHttp2Stream clientStream = client.Local().CreateStream(3, false);
        IHttp2Stream serverStream = server.Local().CreateStream(2, false);
        Assert.Null(clientStream.SetProperty(key, "client"));
        Assert.Null(serverStream.SetProperty(key, "server"));
        Assert.Equal("client", clientStream.GetProperty<string>(key));
        Assert.Equal("server", serverStream.GetProperty<string>(key));
        Assert.Equal("client", clientStream.RemoveProperty<string>(key));
        Assert.Null(clientStream.GetProperty<string>(key));
        Assert.Equal("server", serverStream.GetProperty<string>(key));
    }

    [Fact]
    public void GlobalKeysShouldBeIndependent()
    {
        IHttp2Connection.IPropertyKey keyA = Http2ConnectionPropertyKeys.NewGlobalKey();
        IHttp2Connection.IPropertyKey keyB = Http2ConnectionPropertyKeys.NewGlobalKey();
        IHttp2Stream stream = client.Local().CreateStream(3, false);
        stream.SetProperty(keyA, "a");
        stream.SetProperty(keyB, "b");
        Assert.Equal("a", stream.GetProperty<string>(keyA));
        Assert.Equal("b", stream.GetProperty<string>(keyB));
    }

    [Fact]
    public void UnsetPropertyShouldReturnNullForBothKeyKinds()
    {
        IHttp2Connection.IPropertyKey connectionKey = client.NewKey();
        IHttp2Connection.IPropertyKey globalKey = Http2ConnectionPropertyKeys.NewGlobalKey();
        IHttp2Stream stream = client.Local().CreateStream(3, false);
        Assert.Null(stream.GetProperty<string>(connectionKey));
        Assert.Null(stream.GetProperty<string>(globalKey));
        Assert.Null(stream.RemoveProperty<string>(connectionKey));
        Assert.Null(stream.RemoveProperty<string>(globalKey));
    }

    private sealed class ListenerExceptionThrower
    {
        private readonly bool[] array;
        private readonly int index;

        internal ListenerExceptionThrower(bool[] array, int index)
        {
            this.array = array;
            this.index = index;
        }

        public void Answer()
        {
            array[index] = true;
            throw Http2TestUtil.FAKE_EXCEPTION;
        }
    }

    private sealed class ListenerVerifyCallAnswer
    {
        private readonly bool[] array;
        private readonly int index;

        internal ListenerVerifyCallAnswer(bool[] array, int index)
        {
            this.array = array;
            this.index = index;
        }

        public void Answer()
        {
            Assert.True(array[index]);
        }
    }

    private static T StreamEq<T>(T stream) where T : class
    {
        return stream == null ? Match.Create<T>(s => s == null) : Match.Create<T>(s => Equals(s, stream));
    }
}
