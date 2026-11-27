/*
 * Copyright 2023 The Netty Project
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

using System.Globalization;
using System.Text;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Event that is generated if the remote peer sends a
 * <a href="https://www.rfc-editor.org/rfc/rfc9000#name-connection_close-frames">CLOSE_CONNECTION frame</a>.
 * This allows to inspect the various details of the cause of the close.
 */
// CLR: the package-private fields and constructor are internal. byte[].clone() is Array.Clone(). Arrays.toString(byte[])
// is the private ArraysToString below; toString() keeps Java's boolean text and the invariant culture. System.Net.Quic
// reports a peer's close as a System.Net.Quic.QuicException (QuicError.ConnectionAborted with ApplicationErrorCode,
// docs/quic-design.md, probe) without a reason phrase; the msquic implementation (stage 2) builds this event from it.
public sealed class QuicConnectionCloseEvent : IQuicEvent
{

    internal readonly bool applicationClose;
    internal readonly int error;
    internal readonly byte[] reason;

    internal QuicConnectionCloseEvent(bool applicationClose, int error, byte[] reason)
    {
        this.applicationClose = applicationClose;
        this.error = error;
        this.reason = reason;
    }

    /**
     * Return {@code true} if this was an application close, {@code false} otherwise.
     *
     * @return  if this is an application close.
     */
    public bool IsApplicationClose()
    {
        return applicationClose;
    }

    /**
     * Return the error that was provided for the close.
     *
     * @return the error.
     */
    public int Error()
    {
        return error;
    }

    /**
     * Returns {@code true} if a <a href="https://www.rfc-editor.org/rfc/rfc9001#section-4.8">TLS error</a>
     * is contained.
     * @return {@code true} if this is an {@code TLS error}, {@code false} otherwise.
     */
    public bool IsTlsError()
    {
        return !applicationClose && error >= 0x0100;
    }

    /**
     * Returns the reason for the close, which may be empty if no reason was given as part of the close.
     *
     * @return  the reason.
     */
    public byte[] Reason()
    {
        return (byte[]) reason.Clone();
    }

    public override string ToString()
    {
        return "QuicConnectionCloseEvent{" +
                "applicationClose=" + (applicationClose ? "true" : "false") +
                ", error=" + error.ToString(CultureInfo.InvariantCulture) +
                ", reason=" + ArraysToString(reason) +
                '}';
    }

    /**
     * Extract the contained {@code TLS error} from the {@code QUIC error}. If the given {@code QUIC error} does not
     * contain a {@code TLS error} it will return {@code -1}.
     *
     * @param error the {@code QUIC error}
     * @return      the {@code TLS error} or {@code -1} if there was no {@code TLS error} contained.
     */
    public static int ExtractTlsError(int error)
    {
        int tlsError = error - 0x0100;
        if (tlsError < 0)
        {
            return -1;
        }
        return tlsError;
    }

    // CLR-only helper: Arrays.toString(byte[]) lists the elements "enclosed in square brackets", separated by ", ",
    // converted "as by String.valueOf(byte)" (the signed Java byte), and returns "null" for a null array
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html#toString(byte%5B%5D)), as
    // codec-mqtt MqttConnectPayload maps it.
    private static string ArraysToString(byte[] a)
    {
        if (a == null)
        {
            return "null";
        }
        StringBuilder b = new StringBuilder("[");
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0)
            {
                b.Append(", ");
            }
            b.Append(((sbyte) a[i]).ToString(CultureInfo.InvariantCulture));
        }
        return b.Append(']').ToString();
    }
}
