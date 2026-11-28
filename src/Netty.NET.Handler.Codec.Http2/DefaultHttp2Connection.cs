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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Simple implementation of {@link Http2Connection}.
 */
// CLR: IntObjectMap<Http2Stream>/IntObjectHashMap are Dictionary<int, IHttp2Stream> (owner decision 2026-11-19,
// docs/codec-http-porting.md, Shared CLR adaptations); streamMap.get(id) is GetValueOrDefault. The inner (non-static)
// classes DefaultStream, ConnectionStream, DefaultEndpoint, ActiveStreams, DefaultPropertyKey and PropertyKeyRegistry
// receive the connection explicitly, because a C# nested class has no enclosing instance. close(Promise<Void>) is
// CloseAsync(TaskCompletionSource) and returns the promise's Task; PromiseNotifier.cascade(closePromise, promise) is a
// synchronous continuation of closePromise that copies its outcome with TrySetFromTask (the PromiseNotifier mapping of
// docs/common-task-composition.md); the cascade's back propagation of a cancelled promise has no counterpart. There is
// no void promise (docs/transport-porting.md: VoidChannelPromise has no counterpart), so the branch that replaces a
// void closePromise cannot apply and is left out. The iterator of close() is the Dictionary enumerator:
// Iterator.remove() is Dictionary.Remove of the current key, which "may be safely called without invalidating active
// enumerators" (.NET Core 3.0+;
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.remove?view=net-10.0), so
// close(Iterator) and removeStream(stream, Iterator) take the enumerator only as the marker that the removal happens
// during that iteration. java.util.LinkedHashSet is Common's LinkedHashSet and ArrayDeque is Queue. A Java switch on
// the State enum class is an if/else chain on reference equality (see IHttp2Stream.State). The anonymous Event and
// Http2StreamVisitor classes are private nested classes. Throwable is Exception and the "Caught Throwable" log
// messages are kept. Numbers concatenated into messages use the invariant culture.
public class DefaultHttp2Connection : IHttp2Connection
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DefaultHttp2Connection));
    // Fields accessed by inner classes
    internal readonly Dictionary<int, IHttp2Stream> streamMap = new Dictionary<int, IHttp2Stream>();
    internal readonly PropertyKeyRegistry propertyKeyRegistry;
    internal readonly ConnectionStream_ connectionStream;
    internal readonly DefaultEndpoint<IHttp2LocalFlowController> localEndpoint;
    internal readonly DefaultEndpoint<IHttp2RemoteFlowController> remoteEndpoint;

    /**
     * We chose a {@link List} over a {@link Set} to avoid allocating an {@link Iterator} objects when iterating over
     * the listeners.
     * <p>
     * Initial size of 4 because the default configuration currently has 3 listeners
     * (local/remote flow controller and {@link StreamByteDistributor}) and we leave room for 1 extra.
     * We could be more aggressive but the ArrayList resize will double the size if we are too small.
     */
    internal readonly List<IHttp2Connection.IListener> listeners = new List<IHttp2Connection.IListener>(4);
    internal readonly ActiveStreams activeStreams;
    internal TaskCompletionSource closePromise;

    /**
     * Creates a new connection with the given settings.
     * @param server whether or not this end-point is the server-side of the HTTP/2 connection.
     */
    public DefaultHttp2Connection(bool server) : this(server, DEFAULT_MAX_RESERVED_STREAMS)
    {
    }

    /**
     * Creates a new connection with the given settings.
     * @param server whether or not this end-point is the server-side of the HTTP/2 connection.
     * @param maxReservedStreams The maximum amount of streams which can exist in the reserved state for each endpoint.
     */
    public DefaultHttp2Connection(bool server, int maxReservedStreams)
    {
        // CLR: the field initializers of propertyKeyRegistry and connectionStream need this connection.
        propertyKeyRegistry = new PropertyKeyRegistry();
        connectionStream = new ConnectionStream_(this);
        activeStreams = new ActiveStreams(this, listeners);
        // Reserved streams are excluded from the SETTINGS_MAX_CONCURRENT_STREAMS limit according to [1] and the RFC
        // doesn't define a way to communicate the limit on reserved streams. We rely upon the peer to send RST_STREAM
        // in response to any locally enforced limits being exceeded [2].
        // [1] https://tools.ietf.org/html/rfc7540#section-5.1.2
        // [2] https://tools.ietf.org/html/rfc7540#section-8.2.2
        localEndpoint = new DefaultEndpoint<IHttp2LocalFlowController>(this, server,
            server ? int.MaxValue : maxReservedStreams);
        remoteEndpoint = new DefaultEndpoint<IHttp2RemoteFlowController>(this, !server, maxReservedStreams);

        // Add the connection stream to the map.
        streamMap[connectionStream.Id()] = connectionStream;
    }

    /**
     * Determine if {@link #close(Promise)} has been called and no more streams are allowed to be created.
     */
    internal bool IsClosed()
    {
        return closePromise != null;
    }

    public virtual Task CloseAsync(TaskCompletionSource promise)
    {
        CheckNotNull(promise, "promise");

        // Since we allow this method to be called multiple times, we must make sure that all the promises are notified
        // when all streams are removed and the close operation completes.
        if (closePromise != null)
        {
            if (closePromise == promise)
            {
                // Do nothing
            }
            else
            {
                _ = closePromise.Task.ContinueWith(static (t, state) => ((TaskCompletionSource) state).TrySetFromTask(t),
                    promise, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        else
        {
            closePromise = promise;
        }

        if (IsStreamMapEmpty())
        {
            promise.TrySetResult();
            return promise.Task;
        }

        IEnumerator<KeyValuePair<int, IHttp2Stream>> itr = streamMap.GetEnumerator();

        // We must take care while iterating the streamMap as to not modify while iterating in case there are other code
        // paths iterating over the active streams.
        if (activeStreams.AllowModifications())
        {
            activeStreams.IncrementPendingIterations();

            try
            {
                while (itr.MoveNext())
                {
                    DefaultStream stream = (DefaultStream) itr.Current.Value;
                    if (stream.Id() != CONNECTION_STREAM_ID)
                    {
                        // If modifications of the activeStream map is allowed, then a stream close operation will also
                        // modify the streamMap. Pass the iterator in so that remove will be called to prevent
                        // concurrent modification exceptions.
                        stream.Close(itr);
                    }
                }
            }
            finally
            {
                activeStreams.DecrementPendingIterations();
            }
        }
        else
        {
            while (itr.MoveNext())
            {
                IHttp2Stream stream = itr.Current.Value;
                if (stream.Id() != CONNECTION_STREAM_ID)
                {
                    // We are not allowed to make modifications, so the close calls will be executed after this
                    // iteration completes.
                    stream.Close();
                }
            }
        }

        return closePromise.Task;
    }

    public virtual void AddListener(IHttp2Connection.IListener listener)
    {
        listeners.Add(listener);
    }

    public virtual void RemoveListener(IHttp2Connection.IListener listener)
    {
        listeners.Remove(listener);
    }

    public virtual bool IsServer()
    {
        return localEndpoint.IsServer();
    }

    public virtual IHttp2Stream ConnectionStream()
    {
        return connectionStream;
    }

    public virtual IHttp2Stream Stream(int streamId)
    {
        return streamMap.GetValueOrDefault(streamId);
    }

    public virtual bool StreamMayHaveExisted(int streamId)
    {
        return remoteEndpoint.MayHaveCreatedStream(streamId) || localEndpoint.MayHaveCreatedStream(streamId);
    }

    public virtual int NumActiveStreams()
    {
        return activeStreams.Size();
    }

    public virtual IHttp2Stream ForEachActiveStream(IHttp2StreamVisitor visitor)
    {
        return activeStreams.ForEachActiveStream(visitor);
    }

    public virtual IHttp2Connection.IEndpoint<IHttp2LocalFlowController> Local()
    {
        return localEndpoint;
    }

    public virtual IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> Remote()
    {
        return remoteEndpoint;
    }

    public virtual bool GoAwayReceived()
    {
        return localEndpoint.lastStreamKnownByPeer >= 0;
    }

    public virtual void GoAwayReceived(int lastKnownStream, long errorCode, ByteBuf debugData)
    {
        if (localEndpoint.LastStreamKnownByPeer() >= 0 && localEndpoint.LastStreamKnownByPeer() < lastKnownStream)
        {
            throw ConnectionError(PROTOCOL_ERROR, "lastStreamId MUST NOT increase. Current value: %d new value: %d",
                    localEndpoint.LastStreamKnownByPeer(), lastKnownStream);
        }

        localEndpoint.LastStreamKnownByPeer(lastKnownStream);

        for (int i = 0; i < listeners.Count; ++i)
        {
            try
            {
                listeners[i].OnGoAwayReceived(lastKnownStream, errorCode, debugData);
            }
            catch (Exception cause)
            {
                logger.Error("Caught Throwable from listener onGoAwayReceived.", cause);
            }
        }

        CloseStreamsGreaterThanLastKnownStreamId(lastKnownStream, localEndpoint);
    }

    public virtual bool GoAwaySent()
    {
        return remoteEndpoint.lastStreamKnownByPeer >= 0;
    }

    public virtual bool GoAwaySent(int lastKnownStream, long errorCode, ByteBuf debugData)
    {
        if (remoteEndpoint.LastStreamKnownByPeer() >= 0)
        {
            // Protect against re-entrancy. Could happen if writing the frame fails, and error handling
            // treating this is a connection handler and doing a graceful shutdown...
            if (lastKnownStream == remoteEndpoint.LastStreamKnownByPeer())
            {
                return false;
            }
            if (lastKnownStream > remoteEndpoint.LastStreamKnownByPeer())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Last stream identifier must not increase between " +
                                "sending multiple GOAWAY frames (was '%d', is '%d').",
                        remoteEndpoint.LastStreamKnownByPeer(), lastKnownStream);
            }
        }

        remoteEndpoint.LastStreamKnownByPeer(lastKnownStream);

        for (int i = 0; i < listeners.Count; ++i)
        {
            try
            {
                listeners[i].OnGoAwaySent(lastKnownStream, errorCode, debugData);
            }
            catch (Exception cause)
            {
                logger.Error("Caught Throwable from listener onGoAwaySent.", cause);
            }
        }

        CloseStreamsGreaterThanLastKnownStreamId(lastKnownStream, remoteEndpoint);
        return true;
    }

    private void CloseStreamsGreaterThanLastKnownStreamId(int lastKnownStream,
                                                          DefaultEndpoint endpoint)
    {
        ForEachActiveStream(new CloseStreamsGreaterThanVisitor(lastKnownStream, endpoint));
    }

    // CLR: the anonymous Http2StreamVisitor of closeStreamsGreaterThanLastKnownStreamId.
    private sealed class CloseStreamsGreaterThanVisitor : IHttp2StreamVisitor
    {
        private readonly int lastKnownStream;
        private readonly DefaultEndpoint endpoint;

        internal CloseStreamsGreaterThanVisitor(int lastKnownStream, DefaultEndpoint endpoint)
        {
            this.lastKnownStream = lastKnownStream;
            this.endpoint = endpoint;
        }

        public bool Visit(IHttp2Stream stream)
        {
            if (stream.Id() > lastKnownStream && endpoint.IsValidStreamId(stream.Id()))
            {
                stream.Close();
            }

            return true;
        }
    }

    /**
     * Determine if {@link #streamMap} only contains the connection stream.
     */
    private bool IsStreamMapEmpty()
    {
        return streamMap.Count == 1;
    }

    /**
     * Remove a stream from the {@link #streamMap}.
     * @param stream the stream to remove.
     * @param itr an iterator that may be pointing to the stream during iteration and {@link Iterator#remove()} will be
     * used if non-{@code null}.
     */
    internal void RemoveStream(DefaultStream stream, IEnumerator<KeyValuePair<int, IHttp2Stream>> itr)
    {
        bool removed;

        if (itr == null)
        {
            removed = streamMap.Remove(stream.Id());
        }
        else
        {
            // CLR: itr.remove(): the current key is removed from the Dictionary, which keeps the enumerator valid
            // (see the class comment).
            streamMap.Remove(stream.Id());
            removed = true;
        }

        if (removed)
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                try
                {
                    listeners[i].OnStreamRemoved(stream);
                }
                catch (Exception cause)
                {
                    logger.Error("Caught Throwable from listener onStreamRemoved.", cause);
                }
            }

            if (closePromise != null && IsStreamMapEmpty())
            {
                closePromise.TrySetResult();
            }
        }
    }

    internal static State ActiveState(int streamId, State initialState, bool isLocal, bool halfClosed)
    {
        if (initialState == State.IDLE)
        {
            return halfClosed ? isLocal ? State.HALF_CLOSED_LOCAL : State.HALF_CLOSED_REMOTE : State.OPEN;
        }
        else if (initialState == State.RESERVED_LOCAL)
        {
            return State.HALF_CLOSED_REMOTE;
        }
        else if (initialState == State.RESERVED_REMOTE)
        {
            return State.HALF_CLOSED_LOCAL;
        }
        else
        {
            throw StreamError(streamId, PROTOCOL_ERROR, "Attempting to open a stream in an invalid state: "
                    + initialState);
        }
    }

    internal void NotifyHalfClosed(IHttp2Stream stream)
    {
        for (int i = 0; i < listeners.Count; i++)
        {
            try
            {
                listeners[i].OnStreamHalfClosed(stream);
            }
            catch (Exception cause)
            {
                logger.Error("Caught Throwable from listener onStreamHalfClosed.", cause);
            }
        }
    }

    internal void NotifyClosed(IHttp2Stream stream)
    {
        for (int i = 0; i < listeners.Count; i++)
        {
            try
            {
                listeners[i].OnStreamClosed(stream);
            }
            catch (Exception cause)
            {
                logger.Error("Caught Throwable from listener onStreamClosed.", cause);
            }
        }
    }

    public virtual IHttp2Connection.IPropertyKey NewKey()
    {
        return propertyKeyRegistry.NewKey(this);
    }

    /**
     * Verifies that the key was created by this connection.
     *
     * @throws IllegalArgumentException if the key was not created by this connection.
     */
    internal DefaultPropertyKey VerifyKey(DefaultPropertyKey key)
    {
        return key.VerifyConnection(this);
    }

    /**
     * Simple stream implementation. Streams can be compared to each other by priority.
     */
    // CLR: the inner class receives its connection (outer instance). equals() is reference equality as Java's
    // super.equals(obj), and System.identityHashCode is RuntimeHelpers.GetHashCode, "a hash code that indicates object
    // identity"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode?view=net-10.0).
    // getClass().getSimpleName() is GetType().Name. Java's unchecked (V) casts of the property values are erased and
    // cannot fail; here a missing value or a value of another type gives default(V) (null for reference types).
    internal class DefaultStream : IHttp2Stream
    {
        private const byte META_STATE_SENT_RST = 1;
        private const byte META_STATE_SENT_HEADERS = 1 << 1;
        private const byte META_STATE_SENT_TRAILERS = 1 << 2;
        private const byte META_STATE_SENT_PUSHPROMISE = 1 << 3;
        private const byte META_STATE_RECV_HEADERS = 1 << 4;
        private const byte META_STATE_RECV_TRAILERS = 1 << 5;
        protected readonly DefaultHttp2Connection connection;
        private readonly int id;
        private readonly long identity;
        private readonly PropertyMap properties;
        private State state;
        private byte metaState;

        internal DefaultStream(DefaultHttp2Connection connection, long identity, int id, State state)
        {
            this.connection = connection;
            properties = new PropertyMap(connection);
            this.identity = identity;
            this.id = id;
            this.state = state;
        }

        public int Id()
        {
            return id;
        }

        public State State_()
        {
            return state;
        }

        public virtual bool IsResetSent()
        {
            return (metaState & META_STATE_SENT_RST) != 0;
        }

        public virtual IHttp2Stream ResetSent()
        {
            metaState |= META_STATE_SENT_RST;
            return this;
        }

        public virtual IHttp2Stream HeadersSent(bool isInformational)
        {
            if (!isInformational)
            {
                metaState |= IsHeadersSent() ? META_STATE_SENT_TRAILERS : META_STATE_SENT_HEADERS;
            }

            return this;
        }

        public virtual bool IsHeadersSent()
        {
            return (metaState & META_STATE_SENT_HEADERS) != 0;
        }

        public virtual bool IsTrailersSent()
        {
            return (metaState & META_STATE_SENT_TRAILERS) != 0;
        }

        public virtual IHttp2Stream HeadersReceived(bool isInformational)
        {
            if (!isInformational)
            {
                metaState |= IsHeadersReceived() ? META_STATE_RECV_TRAILERS : META_STATE_RECV_HEADERS;
            }

            return this;
        }

        public virtual bool IsHeadersReceived()
        {
            return (metaState & META_STATE_RECV_HEADERS) != 0;
        }

        public virtual bool IsTrailersReceived()
        {
            return (metaState & META_STATE_RECV_TRAILERS) != 0;
        }

        public virtual IHttp2Stream PushPromiseSent()
        {
            metaState |= META_STATE_SENT_PUSHPROMISE;
            return this;
        }

        public virtual bool IsPushPromiseSent()
        {
            return (metaState & META_STATE_SENT_PUSHPROMISE) != 0;
        }

        public V SetProperty<V>(IHttp2Connection.IPropertyKey key, V value)
        {
            CheckNotNull(key, "key");
            if (key is DefaultPropertyKey)
            {
                return properties.Add(connection.VerifyKey((DefaultPropertyKey) key), value);
            }
            if (key is Http2ConnectionPropertyKeys.GlobalPropertyKey)
            {
                return properties.AddGlobal((Http2ConnectionPropertyKeys.GlobalPropertyKey) key, value);
            }

            throw new ArgumentException("Unsupported key: " + key);
        }

        public V GetProperty<V>(IHttp2Connection.IPropertyKey key)
        {
            CheckNotNull(key, "key");
            if (key is DefaultPropertyKey)
            {
                return properties.Get<V>(connection.VerifyKey((DefaultPropertyKey) key));
            }
            if (key is Http2ConnectionPropertyKeys.GlobalPropertyKey)
            {
                return properties.GetGlobal<V>((Http2ConnectionPropertyKeys.GlobalPropertyKey) key);
            }

            throw new ArgumentException("Unsupported key: " + key);
        }

        public V RemoveProperty<V>(IHttp2Connection.IPropertyKey key)
        {
            CheckNotNull(key, "key");
            if (key is DefaultPropertyKey)
            {
                return properties.Remove<V>(connection.VerifyKey((DefaultPropertyKey) key));
            }
            if (key is Http2ConnectionPropertyKeys.GlobalPropertyKey)
            {
                return properties.RemoveGlobal<V>((Http2ConnectionPropertyKeys.GlobalPropertyKey) key);
            }

            throw new ArgumentException("Unsupported key: " + key);
        }

        public virtual IHttp2Stream Open(bool halfClosed)
        {
            state = ActiveState(id, state, IsLocal(), halfClosed);
            DefaultEndpoint endpoint = CreatedBy();
            if (!endpoint.CanOpenStream())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Maximum active streams violated for this endpoint: " +
                        endpoint.MaxActiveStreams().ToString(CultureInfo.InvariantCulture));
            }

            Activate();
            return this;
        }

        internal void Activate()
        {
            // If the stream is opened in a half-closed state, the headers must have either
            // been sent if this is a local stream, or received if it is a remote stream.
            if (state == State.HALF_CLOSED_LOCAL)
            {
                HeadersSent(/*isInformational*/ false);
            }
            else if (state == State.HALF_CLOSED_REMOTE)
            {
                HeadersReceived(/*isInformational*/ false);
            }

            connection.activeStreams.Activate(this);
        }

        internal IHttp2Stream Close(IEnumerator<KeyValuePair<int, IHttp2Stream>> itr)
        {
            if (state == State.CLOSED)
            {
                return this;
            }

            state = State.CLOSED;

            --CreatedBy().numStreams;
            connection.activeStreams.Deactivate(this, itr);
            return this;
        }

        public virtual IHttp2Stream Close()
        {
            return Close(null);
        }

        public virtual IHttp2Stream CloseLocalSide()
        {
            if (state == State.OPEN)
            {
                state = State.HALF_CLOSED_LOCAL;
                connection.NotifyHalfClosed(this);
            }
            else if (state == State.HALF_CLOSED_LOCAL)
            {
            }
            else
            {
                Close();
            }

            return this;
        }

        public virtual IHttp2Stream CloseRemoteSide()
        {
            if (state == State.OPEN)
            {
                state = State.HALF_CLOSED_REMOTE;
                connection.NotifyHalfClosed(this);
            }
            else if (state == State.HALF_CLOSED_REMOTE)
            {
            }
            else
            {
                Close();
            }

            return this;
        }

        internal virtual DefaultEndpoint CreatedBy()
        {
            return connection.localEndpoint.IsValidStreamId(id) ? connection.localEndpoint : connection.remoteEndpoint;
        }

        internal bool IsLocal()
        {
            return connection.localEndpoint.IsValidStreamId(id);
        }

        /**
         * Provides the lazy initialization for the {@link DefaultStream} data map.
         */
        // CLR: Arrays.copyOf(values, n) is Array.Resize, which copies the elements into a new array of length n
        // (n is larger than the old length here).
        private sealed class PropertyMap
        {
            private readonly DefaultHttp2Connection connection;
            internal object[] values = EmptyArrays.EMPTY_OBJECTS;

            /**
             * Storage for keys that are not tied to this connection. Allocated lazily as such keys are expected to
             * be used rarely, if ever, compared to keys created via {@link Http2Connection#newKey()}.
             */
            internal object[] globalValues = EmptyArrays.EMPTY_OBJECTS;

            internal PropertyMap(DefaultHttp2Connection connection)
            {
                this.connection = connection;
            }

            internal V Add<V>(DefaultPropertyKey key, V value)
            {
                ResizeIfNecessary(key.index);
                V prevValue = Cast<V>(values[key.index]);
                values[key.index] = value;
                return prevValue;
            }

            internal V Get<V>(DefaultPropertyKey key)
            {
                if (key.index >= values.Length)
                {
                    return default;
                }

                return Cast<V>(values[key.index]);
            }

            internal V Remove<V>(DefaultPropertyKey key)
            {
                V prevValue = default;

                if (key.index < values.Length)
                {
                    prevValue = Cast<V>(values[key.index]);
                    values[key.index] = null;
                }

                return prevValue;
            }

            internal void ResizeIfNecessary(int index)
            {
                if (index >= values.Length)
                {
                    Array.Resize(ref values, connection.propertyKeyRegistry.Size());
                }
            }

            internal V AddGlobal<V>(Http2ConnectionPropertyKeys.GlobalPropertyKey key, V value)
            {
                ResizeGlobalIfNecessary(key.index);
                V prevValue = Cast<V>(globalValues[key.index]);
                globalValues[key.index] = value;
                return prevValue;
            }

            internal V GetGlobal<V>(Http2ConnectionPropertyKeys.GlobalPropertyKey key)
            {
                if (key.index >= globalValues.Length)
                {
                    return default;
                }

                return Cast<V>(globalValues[key.index]);
            }

            internal V RemoveGlobal<V>(Http2ConnectionPropertyKeys.GlobalPropertyKey key)
            {
                V prevValue = default;

                if (key.index < globalValues.Length)
                {
                    prevValue = Cast<V>(globalValues[key.index]);
                    globalValues[key.index] = null;
                }

                return prevValue;
            }

            internal void ResizeGlobalIfNecessary(int index)
            {
                if (index >= globalValues.Length)
                {
                    Array.Resize(ref globalValues, Http2ConnectionPropertyKeys.KeyCount());
                }
            }

            // CLR: Java's unchecked cast (V) value is erased, so it never fails here (a caller that ignores the
            // previous value of setProperty never sees the cast); a value of another type gives default(V), as does
            // null.
            private static V Cast<V>(object value)
            {
                return value is V v ? v : default;
            }
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            long value = identity;
            if (value == 0)
            {
                return RuntimeHelpers.GetHashCode(this);
            }

            return (int) (value ^ (value >>> 32));
        }

        public override string ToString()
        {
            return GetType().Name +
                    "{id=" + id.ToString(CultureInfo.InvariantCulture) +
                    ", state=" + state +
                    ", metaState=" + metaState.ToString(CultureInfo.InvariantCulture) +
                    '}';
        }
    }

    /**
     * Stream class representing the connection, itself.
     */
    // CLR: ConnectionStream_: the nested class cannot share the name of the connectionStream() method
    // (ConnectionStream(), an IHttp2Connection member; CS0102, owner rule: trailing underscore).
    internal sealed class ConnectionStream_ : DefaultStream
    {
        internal ConnectionStream_(DefaultHttp2Connection connection) : base(connection, 0, CONNECTION_STREAM_ID, State.IDLE)
        {
        }

        public override bool IsResetSent()
        {
            return false;
        }

        internal override DefaultEndpoint CreatedBy()
        {
            return null;
        }

        public override IHttp2Stream ResetSent()
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream Open(bool halfClosed)
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream Close()
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream CloseLocalSide()
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream CloseRemoteSide()
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream HeadersSent(bool isInformational)
        {
            throw new NotSupportedException();
        }

        public override bool IsHeadersSent()
        {
            throw new NotSupportedException();
        }

        public override IHttp2Stream PushPromiseSent()
        {
            throw new NotSupportedException();
        }

        public override bool IsPushPromiseSent()
        {
            throw new NotSupportedException();
        }
    }

    /**
     * Simple endpoint implementation.
     */
    // CLR: the wildcard DefaultEndpoint<?>/DefaultEndpoint<? extends Http2FlowController> (createdBy(), the endpoint of
    // closeStreamsGreaterThanLastKnownStreamId) is this non-generic abstract base with every member that does not use
    // F, and DefaultEndpoint<F> adds the flow controller, as IHttp2Connection splits IEndpoint/IEndpoint<F> (C# has no
    // wildcard types). The inner class receives its connection.
    internal abstract class DefaultEndpoint : IHttp2Connection.IEndpoint
    {
        protected readonly DefaultHttp2Connection connection;
        private readonly bool server;
        /**
         * This is an always increasing sequence number used to hash {@link DefaultStream} instances.
         */
        private long lastCreatedStreamIdentity;
        /**
         * The minimum stream ID allowed when creating the next stream. This only applies at the time the stream is
         * created. If the ID of the stream being created is less than this value, stream creation will fail. Upon
         * successful creation of a stream, this value is incremented to the next valid stream ID.
         */
        private int nextStreamIdToCreate;
        /**
         * Used for reservation of stream IDs. Stream IDs can be reserved in advance by applications before the streams
         * are actually created.  For example, applications may choose to buffer stream creation attempts as a way of
         * working around {@code SETTINGS_MAX_CONCURRENT_STREAMS}, in which case they will reserve stream IDs for each
         * buffered stream.
         */
        private int nextReservationStreamId;
        internal int lastStreamKnownByPeer = -1;
        private bool pushToAllowed;
        private int maxStreams;
        private int maxActiveStreams;
        private readonly int maxReservedStreams;
        // Fields accessed by inner classes
        internal int numActiveStreams;
        internal int numStreams;

        internal DefaultEndpoint(DefaultHttp2Connection connection, bool server, int maxReservedStreams)
        {
            this.connection = connection;
            this.lastCreatedStreamIdentity = 0;
            this.server = server;

            // Determine the starting stream ID for this endpoint. Client-initiated streams
            // are odd and server-initiated streams are even. Zero is reserved for the
            // connection. Stream 1 is reserved client-initiated stream for responding to an
            // upgrade from HTTP 1.1.
            if (server)
            {
                nextStreamIdToCreate = 2;
                nextReservationStreamId = 0;
            }
            else
            {
                nextStreamIdToCreate = 1;
                // For manually created client-side streams, 1 is reserved for HTTP upgrade, so start at 3.
                nextReservationStreamId = 1;
            }

            // Push is disallowed by default for servers and allowed for clients.
            pushToAllowed = !server;
            maxActiveStreams = int.MaxValue;
            this.maxReservedStreams = CheckPositiveOrZero(maxReservedStreams, "maxReservedStreams");
            UpdateMaxStreams();
        }

        public int IncrementAndGetNextStreamId()
        {
            return nextReservationStreamId >= 0 ? nextReservationStreamId += 2 : nextReservationStreamId;
        }

        private void IncrementExpectedStreamId(int streamId)
        {
            if (streamId > nextReservationStreamId && nextReservationStreamId >= 0)
            {
                nextReservationStreamId = streamId;
            }

            nextStreamIdToCreate = streamId + 2;
            ++numStreams;
        }

        public bool IsValidStreamId(int streamId)
        {
            return streamId > 0 && server == ((streamId & 1) == 0);
        }

        public bool MayHaveCreatedStream(int streamId)
        {
            return IsValidStreamId(streamId) && streamId <= LastStreamCreated();
        }

        public bool CanOpenStream()
        {
            return numActiveStreams < maxActiveStreams;
        }

        // CLR: Java narrows the return type to DefaultStream; the interface member returns IHttp2Stream and is
        // implemented explicitly with the DefaultStream method.
        public DefaultStream CreateStream(int streamId, bool halfClosed)
        {
            State state = ActiveState(streamId, State.IDLE, IsLocal(), halfClosed);

            CheckNewStreamAllowed(streamId, state);

            lastCreatedStreamIdentity++;

            // Create and initialize the stream.
            DefaultStream stream = new DefaultStream(connection, lastCreatedStreamIdentity, streamId, state);

            IncrementExpectedStreamId(streamId);

            AddStream(stream);

            stream.Activate();
            return stream;
        }

        IHttp2Stream IHttp2Connection.IEndpoint.CreateStream(int streamId, bool halfClosed)
        {
            return CreateStream(streamId, halfClosed);
        }

        public bool Created(IHttp2Stream stream)
        {
            return stream is DefaultStream && ((DefaultStream) stream).CreatedBy() == this;
        }

        public bool IsServer()
        {
            return server;
        }

        public DefaultStream ReservePushStream(int streamId, IHttp2Stream parent)
        {
            if (parent == null)
            {
                throw ConnectionError(PROTOCOL_ERROR, "Parent stream missing");
            }
            if (IsLocal() ? !parent.State_().LocalSideOpen() : !parent.State_().RemoteSideOpen())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Stream %d is not open for sending push promise", parent.Id());
            }
            if (!Opposite().AllowPushTo())
            {
                throw ConnectionError(PROTOCOL_ERROR, "Server push not allowed to opposite endpoint");
            }

            State state = IsLocal() ? State.RESERVED_LOCAL : State.RESERVED_REMOTE;
            CheckNewStreamAllowed(streamId, state);

            lastCreatedStreamIdentity++;

            // Create and initialize the stream.
            DefaultStream stream = new DefaultStream(connection, lastCreatedStreamIdentity, streamId, state);

            IncrementExpectedStreamId(streamId);

            // Register the stream.
            AddStream(stream);
            return stream;
        }

        IHttp2Stream IHttp2Connection.IEndpoint.ReservePushStream(int streamId, IHttp2Stream parent)
        {
            return ReservePushStream(streamId, parent);
        }

        private void AddStream(DefaultStream stream)
        {
            // Add the stream to the map and priority tree.
            connection.streamMap[stream.Id()] = stream;

            // Notify the listeners of the event.
            List<IHttp2Connection.IListener> listeners = connection.listeners;

            for (int i = 0; i < listeners.Count; i++)
            {
                try
                {
                    listeners[i].OnStreamAdded(stream);
                }
                catch (Exception cause)
                {
                    logger.Error("Caught Throwable from listener onStreamAdded.", cause);
                }
            }
        }

        public void AllowPushTo(bool allow)
        {
            if (allow && server)
            {
                throw new ArgumentException("Servers do not allow push");
            }

            pushToAllowed = allow;
        }

        public bool AllowPushTo()
        {
            return pushToAllowed;
        }

        public int NumActiveStreams()
        {
            return numActiveStreams;
        }

        public int MaxActiveStreams()
        {
            return maxActiveStreams;
        }

        public void MaxActiveStreams(int maxActiveStreams)
        {
            this.maxActiveStreams = maxActiveStreams;
            UpdateMaxStreams();
        }

        public int LastStreamCreated()
        {
            // Stream ids are always incremented by 2 so just subtract it. This is even ok in the case
            // of nextStreamIdToCreate overflown as it will just return the correct positive number.
            // Use max(...) to ensure we return the correct value for the case when its a client and no stream
            // was created yet.
            return Math.Max(0, nextStreamIdToCreate - 2);
        }

        public int LastStreamKnownByPeer()
        {
            return lastStreamKnownByPeer;
        }

        internal void LastStreamKnownByPeer(int lastKnownStream)
        {
            lastStreamKnownByPeer = lastKnownStream;
        }

        public IHttp2Connection.IEndpoint Opposite()
        {
            return IsLocal() ? connection.remoteEndpoint : connection.localEndpoint;
        }

        private void UpdateMaxStreams()
        {
            maxStreams = (int) Math.Min(int.MaxValue, (long) maxActiveStreams + maxReservedStreams);
        }

        private void CheckNewStreamAllowed(int streamId, State state)
        {
            Debug.Assert(state != State.IDLE);
            if (lastStreamKnownByPeer >= 0 && streamId > lastStreamKnownByPeer)
            {
                throw StreamError(streamId, REFUSED_STREAM,
                        "Cannot create stream %d greater than Last-Stream-ID %d from GOAWAY.",
                        streamId, lastStreamKnownByPeer);
            }

            if (!IsValidStreamId(streamId))
            {
                if (streamId < 0)
                {
                    throw new Http2NoMoreStreamIdsException();
                }

                throw ConnectionError(PROTOCOL_ERROR, "Request stream %d is not correct for %s connection", streamId,
                        server ? "server" : "client");
            }

            // This check must be after all id validated checks, but before the max streams check because it may be
            // recoverable to some degree for handling frames which can be sent on closed streams.
            if (streamId < nextStreamIdToCreate)
            {
                throw ClosedStreamError(PROTOCOL_ERROR, "Request stream %d is behind the next expected stream %d",
                        streamId, nextStreamIdToCreate);
            }
            if (nextStreamIdToCreate <= 0)
            {
                // We exhausted the stream id space that we can use. Let's signal this back but also signal that
                // we still may want to process active streams.
                throw new Http2Exception(REFUSED_STREAM, "Stream IDs are exhausted for this endpoint.",
                        Http2Exception.ShutdownHint.GRACEFUL_SHUTDOWN);
            }

            bool isReserved = state == State.RESERVED_LOCAL || state == State.RESERVED_REMOTE;
            if (!isReserved && !CanOpenStream() || isReserved && numStreams >= maxStreams)
            {
                throw StreamError(streamId, REFUSED_STREAM, "Maximum active streams violated for this endpoint: " +
                        (isReserved ? maxStreams : maxActiveStreams).ToString(CultureInfo.InvariantCulture));
            }
            if (connection.IsClosed())
            {
                throw ConnectionError(INTERNAL_ERROR, "Attempted to create stream id %d after connection was closed",
                                      streamId);
            }
        }

        private bool IsLocal()
        {
            return this == connection.localEndpoint;
        }
    }

    // CLR: the members of DefaultEndpoint<F> that use F (see DefaultEndpoint).
    internal sealed class DefaultEndpoint<F> : DefaultEndpoint, IHttp2Connection.IEndpoint<F>
        where F : class, IHttp2FlowController
    {
        private F flowController;

        internal DefaultEndpoint(DefaultHttp2Connection connection, bool server, int maxReservedStreams)
            : base(connection, server, maxReservedStreams)
        {
        }

        public F FlowController()
        {
            return flowController;
        }

        public void FlowController(F flowController)
        {
            this.flowController = CheckNotNull(flowController, "flowController");
        }
    }

    /**
     * Allows events which would modify the collection of active streams to be queued while iterating via {@link
     * #forEachActiveStream(Http2StreamVisitor)}.
     */
    // CLR: the package-private nested interface takes the I prefix and is internal.
    internal interface IEvent
    {
        /**
         * Trigger the original intention of this event. Expect to modify the active streams list.
         * <p/>
         * If a {@link RuntimeException} object is thrown it will be logged and <strong>not propagated</strong>.
         * Throwing from this method is not supported and is considered a programming error.
         */
        void Process();
    }

    /**
     * Manages the list of currently active streams.  Queues any {@link Event}s that would modify the list of
     * active streams in order to prevent modification while iterating.
     */
    internal sealed class ActiveStreams
    {
        private readonly DefaultHttp2Connection connection;
        private readonly List<IHttp2Connection.IListener> listeners;
        private readonly Queue<IEvent> pendingEvents = new Queue<IEvent>(4);
        private readonly LinkedHashSet<IHttp2Stream> streams = new LinkedHashSet<IHttp2Stream>();
        private int pendingIterations;

        internal ActiveStreams(DefaultHttp2Connection connection, List<IHttp2Connection.IListener> listeners)
        {
            this.connection = connection;
            this.listeners = listeners;
        }

        public int Size()
        {
            return streams.Count;
        }

        public void Activate(DefaultStream stream)
        {
            if (AllowModifications())
            {
                AddToActiveStreams(stream);
            }
            else
            {
                pendingEvents.Enqueue(new ActivateEvent(this, stream));
            }
        }

        public void Deactivate(DefaultStream stream, IEnumerator<KeyValuePair<int, IHttp2Stream>> itr)
        {
            if (AllowModifications() || itr != null)
            {
                RemoveFromActiveStreams(stream, itr);
            }
            else
            {
                pendingEvents.Enqueue(new DeactivateEvent(this, stream, itr));
            }
        }

        public IHttp2Stream ForEachActiveStream(IHttp2StreamVisitor visitor)
        {
            IncrementPendingIterations();

            try
            {
                foreach (IHttp2Stream stream in streams)
                {
                    if (!visitor.Visit(stream))
                    {
                        return stream;
                    }
                }

                return null;
            }
            finally
            {
                DecrementPendingIterations();
            }
        }

        internal void AddToActiveStreams(DefaultStream stream)
        {
            if (streams.Add(stream))
            {
                // Update the number of active streams initiated by the endpoint.
                stream.CreatedBy().numActiveStreams++;

                for (int i = 0; i < listeners.Count; i++)
                {
                    try
                    {
                        listeners[i].OnStreamActive(stream);
                    }
                    catch (Exception cause)
                    {
                        logger.Error("Caught Throwable from listener onStreamActive.", cause);
                    }
                }
            }
        }

        internal void RemoveFromActiveStreams(DefaultStream stream, IEnumerator<KeyValuePair<int, IHttp2Stream>> itr)
        {
            if (streams.Remove(stream))
            {
                // Update the number of active streams initiated by the endpoint.
                stream.CreatedBy().numActiveStreams--;
                connection.NotifyClosed(stream);
            }

            connection.RemoveStream(stream, itr);
        }

        internal bool AllowModifications()
        {
            return pendingIterations == 0;
        }

        internal void IncrementPendingIterations()
        {
            ++pendingIterations;
        }

        internal void DecrementPendingIterations()
        {
            --pendingIterations;

            if (AllowModifications())
            {
                for (;;)
                {
                    if (!pendingEvents.TryDequeue(out IEvent @event))
                    {
                        break;
                    }

                    try
                    {
                        @event.Process();
                    }
                    catch (Exception cause)
                    {
                        logger.Error("Caught Throwable while processing pending ActiveStreams$Event.", cause);
                    }
                }
            }
        }

        // CLR: the anonymous Event of activate(DefaultStream).
        private sealed class ActivateEvent : IEvent
        {
            private readonly ActiveStreams activeStreams;
            private readonly DefaultStream stream;

            internal ActivateEvent(ActiveStreams activeStreams, DefaultStream stream)
            {
                this.activeStreams = activeStreams;
                this.stream = stream;
            }

            public void Process()
            {
                activeStreams.AddToActiveStreams(stream);
            }
        }

        // CLR: the anonymous Event of deactivate(DefaultStream, Iterator).
        private sealed class DeactivateEvent : IEvent
        {
            private readonly ActiveStreams activeStreams;
            private readonly DefaultStream stream;
            private readonly IEnumerator<KeyValuePair<int, IHttp2Stream>> itr;

            internal DeactivateEvent(ActiveStreams activeStreams, DefaultStream stream,
                                     IEnumerator<KeyValuePair<int, IHttp2Stream>> itr)
            {
                this.activeStreams = activeStreams;
                this.stream = stream;
                this.itr = itr;
            }

            public void Process()
            {
                activeStreams.RemoveFromActiveStreams(stream, itr);
            }
        }
    }

    /**
     * Implementation of {@link PropertyKey} that specifies the index position of the property.
     */
    // CLR: the inner class keeps its connection (DefaultHttp2Connection.this).
    internal sealed class DefaultPropertyKey : IHttp2Connection.IPropertyKey
    {
        private readonly DefaultHttp2Connection connection;
        internal readonly int index;

        internal DefaultPropertyKey(DefaultHttp2Connection connection, int index)
        {
            this.connection = connection;
            this.index = index;
        }

        internal DefaultPropertyKey VerifyConnection(IHttp2Connection connection)
        {
            if (connection != this.connection)
            {
                throw new ArgumentException("Using a key that was not created by this connection");
            }

            return this;
        }
    }

    /**
     * A registry of all stream property keys known by this connection.
     */
    // CLR: newKey() receives the connection that the inner DefaultPropertyKey belongs to.
    internal sealed class PropertyKeyRegistry
    {
        /**
         * Initial size of 4 because the default configuration currently has 3 listeners
         * (local/remote flow controller and {@link StreamByteDistributor}) and we leave room for 1 extra.
         * We could be more aggressive but the ArrayList resize will double the size if we are too small.
         */
        internal readonly List<DefaultPropertyKey> keys = new List<DefaultPropertyKey>(4);

        /**
         * Registers a new property key.
         */
        internal DefaultPropertyKey NewKey(DefaultHttp2Connection connection)
        {
            DefaultPropertyKey key = new DefaultPropertyKey(connection, keys.Count);
            keys.Add(key);
            return key;
        }

        internal int Size()
        {
            return keys.Count;
        }
    }
}
