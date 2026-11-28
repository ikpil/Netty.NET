/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Generic;
using System.Globalization;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * <a href="https://www.rfc-editor.org/rfc/rfc9000.html#name-transport-error-codes">
 *     RFC9000 20.1. Transport Error Codes</a>
 */
// CLR: the static initializer is the static constructor, which runs after the static field initializers (the constants
// above it), as the Java static block follows them. List<QuicTransportError>.toArray is List<T>.ToArray().
// IllegalArgumentException is ArgumentException. Objects.hash(code) is spelled out (31 + Long.hashCode) to keep the Java
// value; numbers print with the invariant culture.
public sealed class QuicTransportError
{

    /**
     * An endpoint uses this with CONNECTION_CLOSE to signal that the connection is being closed abruptly in the
     * absence of any error.
     */
    public static readonly QuicTransportError NO_ERROR =
            new QuicTransportError(0x0, "NO_ERROR");

    /**
     * The endpoint encountered an internal error and cannot continue with the connection.
     */
    public static readonly QuicTransportError INTERNAL_ERROR =
            new QuicTransportError(0x1, "INTERNAL_ERROR");

    /**
     * The server refused to accept a new connection.
     */
    public static readonly QuicTransportError CONNECTION_REFUSED =
            new QuicTransportError(0x2, "CONNECTION_REFUSED");

    /**
     * An endpoint received more data than it permitted in its advertised data limits.
     */
    public static readonly QuicTransportError FLOW_CONTROL_ERROR =
            new QuicTransportError(0x3, "FLOW_CONTROL_ERROR");

    /**
     * An endpoint received a frame for a stream identifier that exceeded its advertised stream limit for the
     * corresponding stream type.
     */
    public static readonly QuicTransportError STREAM_LIMIT_ERROR =
            new QuicTransportError(0x4, "STREAM_LIMIT_ERROR");

    /**
     * An endpoint received a frame for a stream that was not in a state that permitted that frame.
     */
    public static readonly QuicTransportError STREAM_STATE_ERROR =
            new QuicTransportError(0x5, "STREAM_STATE_ERROR");

    /**
     * (1) An endpoint received a STREAM frame containing data that exceeded the previously established final size,
     * (2) an endpoint received a STREAM frame or a RESET_STREAM frame containing a final size that was lower than
     * the size of stream data that was already received, or (3) an endpoint received a STREAM frame or a RESET_STREAM
     * frame containing a different final size to the one already established.
     */
    public static readonly QuicTransportError FINAL_SIZE_ERROR =
            new QuicTransportError(0x6, "FINAL_SIZE_ERROR");

    /**
     * An endpoint received a frame that was badly formatted -- for instance, a frame of an unknown type or an ACK
     * frame that has more acknowledgment ranges than the remainder of the packet could carry.
     */
    public static readonly QuicTransportError FRAME_ENCODING_ERROR =
            new QuicTransportError(0x7, "FRAME_ENCODING_ERROR");

    /**
     * An endpoint received transport parameters that were badly formatted, included an invalid value, omitted a
     * mandatory transport parameter, included a forbidden transport parameter, or were otherwise in error.
     */
    public static readonly QuicTransportError TRANSPORT_PARAMETER_ERROR =
            new QuicTransportError(0x8, "TRANSPORT_PARAMETER_ERROR");

    /**
     * The number of connection IDs provided by the peer exceeds the advertised active_connection_id_limit.
     */
    public static readonly QuicTransportError CONNECTION_ID_LIMIT_ERROR =
            new QuicTransportError(0x9, "CONNECTION_ID_LIMIT_ERROR");

    /**
     * An endpoint detected an error with protocol compliance that was not covered by more specific error codes.
     */
    public static readonly QuicTransportError PROTOCOL_VIOLATION =
            new QuicTransportError(0xa, "PROTOCOL_VIOLATION");

    /**
     * A server received a client Initial that contained an invalid Token field.
     */
    public static readonly QuicTransportError INVALID_TOKEN =
            new QuicTransportError(0xb, "INVALID_TOKEN");

    /**
     * The application or application protocol caused the connection to be closed.
     */
    public static readonly QuicTransportError APPLICATION_ERROR =
            new QuicTransportError(0xc, "APPLICATION_ERROR");

    /**
     * An endpoint has received more data in CRYPTO frames than it can buffer.
     */
    public static readonly QuicTransportError CRYPTO_BUFFER_EXCEEDED =
            new QuicTransportError(0xd, "CRYPTO_BUFFER_EXCEEDED");

    /**
     * An endpoint detected errors in performing key updates.
     */
    public static readonly QuicTransportError KEY_UPDATE_ERROR =
            new QuicTransportError(0xe, "KEY_UPDATE_ERROR");

    /**
     * An endpoint has reached the confidentiality or integrity limit for the AEAD algorithm used by the given
     * connection.
     */
    public static readonly QuicTransportError AEAD_LIMIT_REACHED =
            new QuicTransportError(0xf, "AEAD_LIMIT_REACHED");

    /**
     * n endpoint has determined that the network path is incapable of supporting QUIC. An endpoint is unlikely to
     * receive a CONNECTION_CLOSE frame carrying this code except when the path does not support a large enough MTU.
     */
    public static readonly QuicTransportError NO_VIABLE_PATH =
            new QuicTransportError(0x10, "NO_VIABLE_PATH");

    private static readonly QuicTransportError[] INT_TO_ENUM_MAP;
    static QuicTransportError()
    {
        List<QuicTransportError> errorList = new List<QuicTransportError>();
        errorList.Add(NO_ERROR);
        errorList.Add(INTERNAL_ERROR);
        errorList.Add(CONNECTION_REFUSED);
        errorList.Add(FLOW_CONTROL_ERROR);
        errorList.Add(STREAM_LIMIT_ERROR);
        errorList.Add(STREAM_STATE_ERROR);
        errorList.Add(FINAL_SIZE_ERROR);
        errorList.Add(FRAME_ENCODING_ERROR);
        errorList.Add(TRANSPORT_PARAMETER_ERROR);
        errorList.Add(CONNECTION_ID_LIMIT_ERROR);
        errorList.Add(PROTOCOL_VIOLATION);
        errorList.Add(INVALID_TOKEN);
        errorList.Add(APPLICATION_ERROR);
        errorList.Add(CRYPTO_BUFFER_EXCEEDED);
        errorList.Add(KEY_UPDATE_ERROR);
        errorList.Add(AEAD_LIMIT_REACHED);
        errorList.Add(NO_VIABLE_PATH);

        // Crypto errors can have various codes.
        //
        // See https://www.rfc-editor.org/rfc/rfc9000.html#name-transport-error-codes:
        // The cryptographic handshake failed. A range of 256 values is reserved for carrying error codes specific to
        // the cryptographic handshake that is used. Codes for errors occurring when TLS is used for the cryptographic
        // handshake are described in Section 4.8 of [QUIC-TLS].
        for (int i = 0x0100; i <= 0x01ff; i++)
        {
            errorList.Add(new QuicTransportError(i, "CRYPTO_ERROR"));
        }

        INT_TO_ENUM_MAP = errorList.ToArray();
    }
    private readonly long code;
    private readonly string name;

    private QuicTransportError(long code, string name)
    {
        this.code = code;
        this.name = name;
    }

    /**
     * Returns true if this is a {@code CRYPTO_ERROR}.
     */
    public bool IsCryptoError()
    {
        return code >= 0x0100 && code <= 0x01ff;
    }

    /**
     * Returns the name of the error as defined by RFC9000.
     *
     * @return name
     */
    public string Name()
    {
        return name;
    }

    /**
     * Returns the code for this error used on the wire as defined by RFC9000.
     */
    public long Code()
    {
        return code;
    }

    public static QuicTransportError ValueOf(long value)
    {
        if (value > 17)
        {
            value -= 0x0100;
        }

        if (value < 0 || value >= INT_TO_ENUM_MAP.Length)
        {
            throw new ArgumentException("Unknown error code value: " + value.ToString(CultureInfo.InvariantCulture));
        }

        return INT_TO_ENUM_MAP[(int) value];
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        QuicTransportError quicError = (QuicTransportError) o;
        return code == quicError.code;
    }

    public override int GetHashCode()
    {
        return unchecked(31 + (int) (code ^ (code >>> 32)));
    }

    public override string ToString()
    {
        return "QuicTransportError{" +
                "code=" + code.ToString(CultureInfo.InvariantCulture) +
                ", name='" + name + '\'' +
                '}';
    }
}
