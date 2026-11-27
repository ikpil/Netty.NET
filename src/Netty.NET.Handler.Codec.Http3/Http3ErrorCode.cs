/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Different <a href="https://datatracker.ietf.org/doc/html/rfc9114#name-http-3-error-codes">HTTP3 error codes</a>.
 */
// CLR: the Java enum with a constructor, a field and a method is a sealed class with static readonly instances, as
// codec-http2 Http2Error and codec-base Base64Dialect (docs/codec-http2-porting.md, Shared CLR adaptations): "You can't
// define a method inside the definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). The instances are compared
// by reference, the implicit values() is Values() (a new array per call, as Java's values() clones) and ToString()
// returns the constant name. The package-private field code is internal.
public sealed class Http3ErrorCode
{

    /**
     * Datagram or Capsule Protocol parse error
     * <a href="https://www.rfc-editor.org/rfc/rfc9297.html#name-http-3-error-code">rfc9297</a>
     * registered in IANA http3
     * <a href="https://www.iana.org/assignments/http3-parameters/http3-parameters.xhtml#http3-parameters-error-codes"
     * >
     *     IANA Http3 Error Codes</a>
     */
    public static readonly Http3ErrorCode H3_DATAGRAM_ERROR = new Http3ErrorCode(nameof(H3_DATAGRAM_ERROR), 0x33);

    /**
     *  No error. This is used when the connection or stream needs to be closed, but there is no error to signal.
     */
    public static readonly Http3ErrorCode H3_NO_ERROR = new Http3ErrorCode(nameof(H3_NO_ERROR), 0x100);

    /**
     * Peer violated protocol requirements in a way that does not match a more specific error code,
     * or endpoint declines to use the more specific error code.
     */
    public static readonly Http3ErrorCode H3_GENERAL_PROTOCOL_ERROR =
        new Http3ErrorCode(nameof(H3_GENERAL_PROTOCOL_ERROR), 0x101);

    /**
     * An internal error has occurred in the HTTP stack.
     */
    public static readonly Http3ErrorCode H3_INTERNAL_ERROR = new Http3ErrorCode(nameof(H3_INTERNAL_ERROR), 0x102);

    /**
     * The endpoint detected that its peer created a stream that it will not accept.
     */
    public static readonly Http3ErrorCode H3_STREAM_CREATION_ERROR =
        new Http3ErrorCode(nameof(H3_STREAM_CREATION_ERROR), 0x103);

    /**
     * A stream required by the HTTP/3 connection was closed or reset.
     */
    public static readonly Http3ErrorCode H3_CLOSED_CRITICAL_STREAM =
        new Http3ErrorCode(nameof(H3_CLOSED_CRITICAL_STREAM), 0x104);

    /**
     * A frame was received that was not permitted in the current state or on the current stream.
     */
    public static readonly Http3ErrorCode H3_FRAME_UNEXPECTED = new Http3ErrorCode(nameof(H3_FRAME_UNEXPECTED), 0x105);

    /**
     * A frame that fails to satisfy layout requirements or with an invalid size was received.
     */
    public static readonly Http3ErrorCode H3_FRAME_ERROR = new Http3ErrorCode(nameof(H3_FRAME_ERROR), 0x106);

    /**
     * The endpoint detected that its peer is exhibiting a behavior that might be generating excessive load.
     */
    public static readonly Http3ErrorCode H3_EXCESSIVE_LOAD = new Http3ErrorCode(nameof(H3_EXCESSIVE_LOAD), 0x107);

    /**
     * A Stream ID or Push ID was used incorrectly, such as exceeding a limit, reducing a limit, or being reused.
     */
    public static readonly Http3ErrorCode H3_ID_ERROR = new Http3ErrorCode(nameof(H3_ID_ERROR), 0x108);

    /**
     * An endpoint detected an error in the payload of a SETTINGS frame.
     */
    public static readonly Http3ErrorCode H3_SETTINGS_ERROR = new Http3ErrorCode(nameof(H3_SETTINGS_ERROR), 0x109);

    /**
     * No SETTINGS frame was received at the beginning of the control stream.
     */
    public static readonly Http3ErrorCode H3_MISSING_SETTINGS = new Http3ErrorCode(nameof(H3_MISSING_SETTINGS), 0x10a);

    /**
     * A server rejected a request without performing any application processing.
     */
    public static readonly Http3ErrorCode H3_REQUEST_REJECTED = new Http3ErrorCode(nameof(H3_REQUEST_REJECTED), 0x10b);

    /**
     * The request or its response (including pushed response) is cancelled.
     */
    public static readonly Http3ErrorCode H3_REQUEST_CANCELLED =
        new Http3ErrorCode(nameof(H3_REQUEST_CANCELLED), 0x10c);

    /**
     * The client's stream terminated without containing a fully-formed request.
     */
    public static readonly Http3ErrorCode H3_REQUEST_INCOMPLETE =
        new Http3ErrorCode(nameof(H3_REQUEST_INCOMPLETE), 0x10d);

    /**
     * An HTTP message was malformed and cannot be processed.
     */
    public static readonly Http3ErrorCode H3_MESSAGE_ERROR = new Http3ErrorCode(nameof(H3_MESSAGE_ERROR), 0x10e);

    /**
     * The TCP connection established in response to a CONNECT request was reset or abnormally closed.
     */
    public static readonly Http3ErrorCode H3_CONNECT_ERROR = new Http3ErrorCode(nameof(H3_CONNECT_ERROR), 0x10f);

    /**
     * The requested operation cannot be served over HTTP/3. The peer should retry over HTTP/1.1.
     */
    public static readonly Http3ErrorCode H3_VERSION_FALLBACK = new Http3ErrorCode(nameof(H3_VERSION_FALLBACK), 0x110);

    /**
     * The decoder failed to interpret an encoded field section and is not able to continue decoding that field section.
     */
    public static readonly Http3ErrorCode QPACK_DECOMPRESSION_FAILED =
        new Http3ErrorCode(nameof(QPACK_DECOMPRESSION_FAILED), 0x200);

    /**
     * The decoder failed to interpret an encoder instruction received on the encoder stream.
     */
    public static readonly Http3ErrorCode QPACK_ENCODER_STREAM_ERROR =
        new Http3ErrorCode(nameof(QPACK_ENCODER_STREAM_ERROR), 0x201);

    /**
     * The encoder failed to interpret a decoder instruction received on the decoder stream.
     */
    public static readonly Http3ErrorCode QPACK_DECODER_STREAM_ERROR =
        new Http3ErrorCode(nameof(QPACK_DECODER_STREAM_ERROR), 0x202);

    private readonly string name;
    internal readonly int code;

    private Http3ErrorCode(string name, int code)
    {
        this.name = name;
        this.code = code;
    }

    // CLR: Java's implicit values().
    public static Http3ErrorCode[] Values()
    {
        return new[]
        {
            H3_DATAGRAM_ERROR, H3_NO_ERROR, H3_GENERAL_PROTOCOL_ERROR, H3_INTERNAL_ERROR, H3_STREAM_CREATION_ERROR,
            H3_CLOSED_CRITICAL_STREAM, H3_FRAME_UNEXPECTED, H3_FRAME_ERROR, H3_EXCESSIVE_LOAD, H3_ID_ERROR,
            H3_SETTINGS_ERROR, H3_MISSING_SETTINGS, H3_REQUEST_REJECTED, H3_REQUEST_CANCELLED, H3_REQUEST_INCOMPLETE,
            H3_MESSAGE_ERROR, H3_CONNECT_ERROR, H3_VERSION_FALLBACK, QPACK_DECOMPRESSION_FAILED,
            QPACK_ENCODER_STREAM_ERROR, QPACK_DECODER_STREAM_ERROR
        };
    }

    public int Code()
    {
        return code;
    }

    // CLR: Enum.toString() is the constant name.
    public override string ToString()
    {
        return name;
    }
}
