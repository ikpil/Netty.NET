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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A single stream within an HTTP2 connection. Streams are compared to each other by priority.
 */
// CLR: the generic property accessors <V> V setProperty(...) are generic methods; Java's unchecked cast of the stored
// object to V is a cast in the implementation, and a missing property is default(V) (null for reference types).
public interface IHttp2Stream
{

    /**
     * The allowed states of an HTTP2 stream.
     */
    // CLR: the Java enum with a constructor, fields and methods is a sealed class with static readonly instances.
    // Its instances are compared by reference, so a Java switch on a State is an if/else chain on reference
    // equality (a C# case label must be a constant, CS0150). ToString() returns the constant name.
    public sealed class State
    {
        public static readonly State IDLE = new State(nameof(IDLE), false, false);
        public static readonly State RESERVED_LOCAL = new State(nameof(RESERVED_LOCAL), false, false);
        public static readonly State RESERVED_REMOTE = new State(nameof(RESERVED_REMOTE), false, false);
        public static readonly State OPEN = new State(nameof(OPEN), true, true);
        public static readonly State HALF_CLOSED_LOCAL = new State(nameof(HALF_CLOSED_LOCAL), false, true);
        public static readonly State HALF_CLOSED_REMOTE = new State(nameof(HALF_CLOSED_REMOTE), true, false);
        public static readonly State CLOSED = new State(nameof(CLOSED), false, false);

        private readonly string name;
        private readonly bool localSideOpen;
        private readonly bool remoteSideOpen;

        private State(string name, bool localSideOpen, bool remoteSideOpen)
        {
            this.name = name;
            this.localSideOpen = localSideOpen;
            this.remoteSideOpen = remoteSideOpen;
        }

        /**
         * Indicates whether the local side of this stream is open (i.e. the state is either
         * {@link State#OPEN} or {@link State#HALF_CLOSED_REMOTE}).
         */
        public bool LocalSideOpen()
        {
            return localSideOpen;
        }

        /**
         * Indicates whether the remote side of this stream is open (i.e. the state is either
         * {@link State#OPEN} or {@link State#HALF_CLOSED_LOCAL}).
         */
        public bool RemoteSideOpen()
        {
            return remoteSideOpen;
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Gets the unique identifier for this stream within the connection.
     */
    int Id();

    /**
     * Gets the state of this stream.
     */
    // CLR: state() is State_(): a member cannot share the name of the nested State type (CS0102, owner rule: trailing
    // underscore).
    State State_();

    /**
     * Opens this stream, making it available via {@link Http2Connection#forEachActiveStream(Http2StreamVisitor)} and
     * transition state to:
     * <ul>
     * <li>{@link State#OPEN} if {@link #state()} is {@link State#IDLE} and {@code halfClosed} is {@code false}.</li>
     * <li>{@link State#HALF_CLOSED_LOCAL} if {@link #state()} is {@link State#IDLE} and {@code halfClosed}
     * is {@code true} and the stream is local. In this state, {@link #isHeadersSent()} is {@code true}</li>
     * <li>{@link State#HALF_CLOSED_REMOTE} if {@link #state()} is {@link State#IDLE} and {@code halfClosed}
     * is {@code true} and the stream is remote. In this state, {@link #isHeadersReceived()} is {@code true}</li>
     * <li>{@link State#RESERVED_LOCAL} if {@link #state()} is {@link State#HALF_CLOSED_REMOTE}.</li>
     * <li>{@link State#RESERVED_REMOTE} if {@link #state()} is {@link State#HALF_CLOSED_LOCAL}.</li>
     * </ul>
     */
    IHttp2Stream Open(bool halfClosed);

    /**
     * Closes the stream.
     */
    IHttp2Stream Close();

    /**
     * Closes the local side of this stream. If this makes the stream closed, the child is closed as
     * well.
     */
    IHttp2Stream CloseLocalSide();

    /**
     * Closes the remote side of this stream. If this makes the stream closed, the child is closed
     * as well.
     */
    IHttp2Stream CloseRemoteSide();

    /**
     * Indicates whether a {@code RST_STREAM} frame has been sent from the local endpoint for this stream.
     */
    bool IsResetSent();

    /**
     * Sets the flag indicating that a {@code RST_STREAM} frame has been sent from the local endpoint
     * for this stream. This does not affect the stream state.
     */
    IHttp2Stream ResetSent();

    /**
     * Associates the application-defined data with this stream.
     * @return The value that was previously associated with {@code key}, or {@code null} if there was none.
     */
    V SetProperty<V>(IHttp2Connection.IPropertyKey key, V value);

    /**
     * Returns application-defined data if any was associated with this stream.
     */
    V GetProperty<V>(IHttp2Connection.IPropertyKey key);

    /**
     * Returns and removes application-defined data if any was associated with this stream.
     */
    V RemoveProperty<V>(IHttp2Connection.IPropertyKey key);

    /**
     * Indicates that headers have been sent to the remote endpoint on this stream. The first call to this method would
     * be for the initial headers (see {@link #isHeadersSent()}} and the second call would indicate the trailers
     * (see {@link #isTrailersReceived()}).
     * @param isInformational {@code true} if the headers contain an informational status code (for responses only).
     */
    IHttp2Stream HeadersSent(bool isInformational);

    /**
     * Indicates whether or not headers were sent to the remote endpoint.
     */
    bool IsHeadersSent();

    /**
     * Indicates whether or not trailers were sent to the remote endpoint.
     */
    bool IsTrailersSent();

    /**
     * Indicates that headers have been received. The first call to this method would be for the initial headers
     * (see {@link #isHeadersReceived()}} and the second call would indicate the trailers
     * (see {@link #isTrailersReceived()}).
     * @param isInformational {@code true} if the headers contain an informational status code (for responses only).
     */
    IHttp2Stream HeadersReceived(bool isInformational);

    /**
     * Indicates whether or not the initial headers have been received.
     */
    bool IsHeadersReceived();

    /**
     * Indicates whether or not the trailers have been received.
     */
    bool IsTrailersReceived();

    /**
     * Indicates that a push promise was sent to the remote endpoint.
     */
    IHttp2Stream PushPromiseSent();

    /**
     * Indicates whether or not a push promise was sent to the remote endpoint.
     */
    bool IsPushPromiseSent();
}
