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

using System.Globalization;
using System.Text;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides utility methods for accessing specific flags as defined by the HTTP/2 spec.
 */
// CLR: ~mask is an int in C#, so the compound assignment casts it back to short (C# compound assignment needs the
// operand to convert implicitly to the variable type). getClass() is GetType(); the short value is printed with the
// invariant culture.
public sealed class Http2Flags
{
    public const short END_STREAM = 0x1;
    public const short END_HEADERS = 0x4;
    public const short ACK = 0x1;
    public const short PADDED = 0x8;
    public const short PRIORITY = 0x20;

    private short value;

    public Http2Flags()
    {
    }

    public Http2Flags(short value)
    {
        this.value = value;
    }

    /**
     * Gets the underlying flags value.
     */
    public short Value()
    {
        return value;
    }

    /**
     * Determines whether the {@link #END_STREAM} flag is set. Only applies to DATA and HEADERS
     * frames.
     */
    public bool EndOfStream()
    {
        return IsFlagSet(END_STREAM);
    }

    /**
     * Determines whether the {@link #END_HEADERS} flag is set. Only applies for HEADERS,
     * PUSH_PROMISE, and CONTINUATION frames.
     */
    public bool EndOfHeaders()
    {
        return IsFlagSet(END_HEADERS);
    }

    /**
     * Determines whether the flag is set indicating the presence of the exclusive, stream
     * dependency, and weight fields in a HEADERS frame.
     */
    public bool PriorityPresent()
    {
        return IsFlagSet(PRIORITY);
    }

    /**
     * Determines whether the flag is set indicating that this frame is an ACK. Only applies for
     * SETTINGS and PING frames.
     */
    public bool Ack()
    {
        return IsFlagSet(ACK);
    }

    /**
     * For frames that include padding, indicates if the {@link #PADDED} field is present. Only
     * applies to DATA, HEADERS, PUSH_PROMISE and CONTINUATION frames.
     */
    public bool PaddingPresent()
    {
        return IsFlagSet(PADDED);
    }

    /**
     * Gets the number of bytes expected for the priority fields of the payload. This is determined
     * by the {@link #priorityPresent()} flag.
     */
    public int GetNumPriorityBytes()
    {
        return PriorityPresent() ? 5 : 0;
    }

    /**
     * Gets the length in bytes of the padding presence field expected in the payload. This is
     * determined by the {@link #paddingPresent()} flag.
     */
    public int GetPaddingPresenceFieldLength()
    {
        return PaddingPresent() ? 1 : 0;
    }

    /**
     * Sets the {@link #END_STREAM} flag.
     */
    public Http2Flags EndOfStream(bool endOfStream)
    {
        return SetFlag(endOfStream, END_STREAM);
    }

    /**
     * Sets the {@link #END_HEADERS} flag.
     */
    public Http2Flags EndOfHeaders(bool endOfHeaders)
    {
        return SetFlag(endOfHeaders, END_HEADERS);
    }

    /**
     * Sets the {@link #PRIORITY} flag.
     */
    public Http2Flags PriorityPresent(bool priorityPresent)
    {
        return SetFlag(priorityPresent, PRIORITY);
    }

    /**
     * Sets the {@link #PADDED} flag.
     */
    public Http2Flags PaddingPresent(bool paddingPresent)
    {
        return SetFlag(paddingPresent, PADDED);
    }

    /**
     * Sets the {@link #ACK} flag.
     */
    public Http2Flags Ack(bool ack)
    {
        return SetFlag(ack, ACK);
    }

    /**
     * Generic method to set any flag.
     * @param on if the flag should be enabled or disabled.
     * @param mask the mask that identifies the bit for the flag.
     * @return this instance.
     */
    public Http2Flags SetFlag(bool on, short mask)
    {
        if (on)
        {
            value |= mask;
        }
        else
        {
            value &= (short) ~mask;
        }
        return this;
    }

    /**
     * Indicates whether or not a particular flag is set.
     * @param mask the mask identifying the bit for the particular flag being tested
     * @return {@code true} if the flag is set
     */
    public bool IsFlagSet(short mask)
    {
        return (value & mask) != 0;
    }

    public override int GetHashCode()
    {
        int prime = 31;
        int result = 1;
        result = prime * result + value;
        return result;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (obj == null)
        {
            return false;
        }
        if (GetType() != obj.GetType())
        {
            return false;
        }

        return value == ((Http2Flags) obj).value;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("value = ").Append(value.ToString(CultureInfo.InvariantCulture)).Append(" (");
        if (Ack())
        {
            builder.Append("ACK,");
        }
        if (EndOfHeaders())
        {
            builder.Append("END_OF_HEADERS,");
        }
        if (EndOfStream())
        {
            builder.Append("END_OF_STREAM,");
        }
        if (PriorityPresent())
        {
            builder.Append("PRIORITY_PRESENT,");
        }
        if (PaddingPresent())
        {
            builder.Append("PADDING_PRESENT,");
        }
        builder.Append(')');
        return builder.ToString();
    }
}
