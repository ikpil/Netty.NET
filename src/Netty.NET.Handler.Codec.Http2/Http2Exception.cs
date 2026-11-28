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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Exception thrown when an HTTP/2 error was encountered.
 */
// CLR: the checked Java exception is a CLR Exception; serialVersionUID is JVM serialization metadata and has no
// counterpart. The constructors without a message call base(), so Message is the CLR default text where Java's
// getMessage() is null (docs/codec-http-porting.md: "never null in .NET"). shutdownHint() is ShutdownHint_(): a member
// cannot share the name of the nested ShutdownHint enum (CS0102, owner rule: trailing underscore); ShutdownHint is a
// C# enum, a value type, so checkNotNull(shutdownHint) has nothing to check. String.format(fmt, args) is
// Http2CodecUtil.Format, which keeps the Java text of the %d, %s and %b conversions that this package uses.
// Iterable<StreamException> is IEnumerable<StreamException>, whose GetEnumerator() is iterator().
public class Http2Exception : Exception
{
    private readonly Http2Error error;
    private readonly ShutdownHint shutdownHint;

    public Http2Exception(Http2Error error) : this(error, ShutdownHint.HARD_SHUTDOWN)
    {
    }

    public Http2Exception(Http2Error error, ShutdownHint shutdownHint)
    {
        this.error = CheckNotNull(error, "error");
        this.shutdownHint = shutdownHint;
    }

    public Http2Exception(Http2Error error, string message) : this(error, message, ShutdownHint.HARD_SHUTDOWN)
    {
    }

    public Http2Exception(Http2Error error, string message, ShutdownHint shutdownHint) : base(message)
    {
        this.error = CheckNotNull(error, "error");
        this.shutdownHint = shutdownHint;
    }

    public Http2Exception(Http2Error error, string message, Exception cause)
        : this(error, message, cause, ShutdownHint.HARD_SHUTDOWN)
    {
    }

    public Http2Exception(Http2Error error, string message, Exception cause, ShutdownHint shutdownHint)
        : base(message, cause)
    {
        this.error = CheckNotNull(error, "error");
        this.shutdownHint = shutdownHint;
    }

    internal static Http2Exception NewStatic(Http2Error error, string message, ShutdownHint shutdownHint,
                                    Type clazz, string method)
    {
        Http2Exception exception = new StacklessHttp2Exception(error, message, shutdownHint, true);
        return ThrowableUtil.UnknownStackTrace(exception, clazz, method);
    }

    // CLR: super(message, null, false, true): the CLR Exception has no suppression or writable-stack-trace switches;
    // the stackless subclass freezes its trace instead.
    private Http2Exception(Http2Error error, string message, ShutdownHint shutdownHint, bool shared) : base(message)
    {
        Debug.Assert(shared);
        this.error = CheckNotNull(error, "error");
        this.shutdownHint = shutdownHint;
    }

    public Http2Error Error()
    {
        return error;
    }

    /**
     * Provide a hint as to what type of shutdown should be executed. Note this hint may be ignored.
     */
    public ShutdownHint ShutdownHint_()
    {
        return shutdownHint;
    }

    /**
     * Use if an error has occurred which can not be isolated to a single stream, but instead applies
     * to the entire connection.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return An exception which can be translated into an HTTP/2 error.
     */
    public static Http2Exception ConnectionError(Http2Error error, string fmt, params object[] args)
    {
        return new Http2Exception(error, FormatErrorMessage(fmt, args));
    }

    /**
     * Use if an error has occurred which can not be isolated to a single stream, but instead applies
     * to the entire connection.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param cause The object which caused the error.
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return An exception which can be translated into an HTTP/2 error.
     */
    public static Http2Exception ConnectionError(Http2Error error, Exception cause,
            string fmt, params object[] args)
    {
        return new Http2Exception(error, FormatErrorMessage(fmt, args), cause);
    }

    /**
     * Use if an error has occurred which can not be isolated to a single stream, but instead applies
     * to the entire connection.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return An exception which can be translated into an HTTP/2 error.
     */
    public static Http2Exception ClosedStreamError(Http2Error error, string fmt, params object[] args)
    {
        return new ClosedStreamCreationException(error, FormatErrorMessage(fmt, args));
    }

    /**
     * Use if an error which can be isolated to a single stream has occurred.  If the {@code id} is not
     * {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a {@link StreamException} will be returned.
     * Otherwise the error is considered a connection error and a {@link Http2Exception} is returned.
     * @param id The stream id for which the error is isolated to.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return If the {@code id} is not
     * {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a {@link StreamException} will be returned.
     * Otherwise the error is considered a connection error and a {@link Http2Exception} is returned.
     */
    public static Http2Exception StreamError(int id, Http2Error error, string fmt, params object[] args)
    {
        return CONNECTION_STREAM_ID == id ?
                ConnectionError(error, fmt, args) :
                    new StreamException(id, error, FormatErrorMessage(fmt, args));
    }

    /**
     * Use if an error which can be isolated to a single stream has occurred.  If the {@code id} is not
     * {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a {@link StreamException} will be returned.
     * Otherwise the error is considered a connection error and a {@link Http2Exception} is returned.
     * @param id The stream id for which the error is isolated to.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param cause The object which caused the error.
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return If the {@code id} is not
     * {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a {@link StreamException} will be returned.
     * Otherwise the error is considered a connection error and a {@link Http2Exception} is returned.
     */
    public static Http2Exception StreamError(int id, Http2Error error, Exception cause,
            string fmt, params object[] args)
    {
        return CONNECTION_STREAM_ID == id ?
                ConnectionError(error, cause, fmt, args) :
                    new StreamException(id, error, FormatErrorMessage(fmt, args), cause);
    }

    /**
     * A specific stream error resulting from failing to decode headers that exceeds the max header size list.
     * If the {@code id} is not {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a
     * {@link StreamException} will be returned. Otherwise the error is considered a
     * connection error and a {@link Http2Exception} is returned.
     * @param id The stream id for which the error is isolated to.
     * @param error The type of error as defined by the HTTP/2 specification.
     * @param onDecode Whether this error was caught while decoding headers
     * @param fmt String with the content and format for the additional debug data.
     * @param args Objects which fit into the format defined by {@code fmt}.
     * @return If the {@code id} is not
     * {@link Http2CodecUtil#CONNECTION_STREAM_ID} then a {@link HeaderListSizeException}
     * will be returned. Otherwise the error is considered a connection error and a {@link Http2Exception} is
     * returned.
     */
    public static Http2Exception HeaderListSizeError(int id, Http2Error error, bool onDecode,
            string fmt, params object[] args)
    {
        return CONNECTION_STREAM_ID == id ?
                ConnectionError(error, fmt, args) :
                    new HeaderListSizeException(id, error, FormatErrorMessage(fmt, args), onDecode);
    }

    // CLR: Arrays.toString(args) is "[" + the String.valueOf text of each element joined with ", " + "]"
    // (Http2CodecUtil.ToJavaString); String.format is Http2CodecUtil.Format.
    private static string FormatErrorMessage(string fmt, object[] args)
    {
        if (fmt == null)
        {
            if (args == null || args.Length == 0)
            {
                return "Unexpected error";
            }

            StringBuilder sb = new StringBuilder("Unexpected error: [");

            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(ToJavaString(args[i]));
            }

            return sb.Append(']').ToString();
        }

        return Format(fmt, args);
    }

    /**
     * Check if an exception is isolated to a single stream or the entire connection.
     * @param e The exception to check.
     * @return {@code true} if {@code e} is an instance of {@link StreamException}.
     * {@code false} otherwise.
     */
    public static bool IsStreamError(Http2Exception e)
    {
        return e is StreamException;
    }

    /**
     * Get the stream id associated with an exception.
     * @param e The exception to get the stream id for.
     * @return {@link Http2CodecUtil#CONNECTION_STREAM_ID} if {@code e} is a connection error.
     * Otherwise the stream id associated with the stream error.
     */
    public static int StreamId(Http2Exception e)
    {
        return IsStreamError(e) ? ((StreamException) e).StreamId() : CONNECTION_STREAM_ID;
    }

    /**
     * Provides a hint as to if shutdown is justified, what type of shutdown should be executed.
     */
    public enum ShutdownHint
    {
        /**
         * Do not shutdown the underlying channel.
         */
        NO_SHUTDOWN,
        /**
         * Attempt to execute a "graceful" shutdown. The definition of "graceful" is left to the implementation.
         * An example of "graceful" would be wait for some amount of time until all active streams are closed.
         */
        GRACEFUL_SHUTDOWN,
        /**
         * Close the channel immediately after a {@code GOAWAY} is sent.
         */
        HARD_SHUTDOWN
    }

    /**
     * Used when a stream creation attempt fails but may be because the stream was previously closed.
     */
    public sealed class ClosedStreamCreationException : Http2Exception
    {

        public ClosedStreamCreationException(Http2Error error) : base(error)
        {
        }

        public ClosedStreamCreationException(Http2Error error, string message) : base(error, message)
        {
        }

        public ClosedStreamCreationException(Http2Error error, string message, Exception cause)
            : base(error, message, cause)
        {
        }
    }

    /**
     * Represents an exception that can be isolated to a single stream (as opposed to the entire connection).
     */
    // CLR: the package-private constructors are internal.
    public class StreamException : Http2Exception
    {
        private readonly int streamId;

        internal StreamException(int streamId, Http2Error error, string message)
            : base(error, message, Http2Exception.ShutdownHint.NO_SHUTDOWN)
        {
            this.streamId = streamId;
        }

        internal StreamException(int streamId, Http2Error error, string message, Exception cause)
            : base(error, message, cause, Http2Exception.ShutdownHint.NO_SHUTDOWN)
        {
            this.streamId = streamId;
        }

        public int StreamId()
        {
            return streamId;
        }
    }

    // CLR: the package-private constructor is internal.
    public sealed class HeaderListSizeException : StreamException
    {

        private readonly bool decode;

        internal HeaderListSizeException(int streamId, Http2Error error, string message, bool decode)
            : base(streamId, error, message)
        {
            this.decode = decode;
        }

        public bool DuringDecode()
        {
            return decode;
        }
    }

    /**
     * Provides the ability to handle multiple stream exceptions with one throw statement.
     */
    public sealed class CompositeStreamException : Http2Exception, IEnumerable<StreamException>
    {
        private readonly List<StreamException> exceptions;

        public CompositeStreamException(Http2Error error, int initialCapacity)
            : base(error, Http2Exception.ShutdownHint.NO_SHUTDOWN)
        {
            exceptions = new List<StreamException>(initialCapacity);
        }

        public void Add(StreamException e)
        {
            exceptions.Add(e);
        }

        public IEnumerator<StreamException> GetEnumerator()
        {
            return exceptions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class StacklessHttp2Exception : Http2Exception
    {

        internal StacklessHttp2Exception(Http2Error error, string message, ShutdownHint shutdownHint)
            : base(error, message, shutdownHint)
        {
        }

        internal StacklessHttp2Exception(Http2Error error, string message, ShutdownHint shutdownHint, bool shared)
            : base(error, message, shutdownHint, shared)
        {
        }

        // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
        // Classloader.
        // CLR: there is no fillInStackTrace(). The first non-null trace (the origin frame stored by
        // ThrowableUtil.UnknownStackTrace) is frozen, so later throws or awaits do not add frames, as codec-http's
        // SpdyProtocolException.StacklessSpdyProtocolException does.
        private string _stackTrace;

        public override string StackTrace => _stackTrace ??= base.StackTrace;
    }
}
