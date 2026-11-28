/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * The SPDY stream status code and its description.
 */
// CLR: Comparable<SpdyStreamStatus> is IComparable<SpdyStreamStatus>; hashCode/equals/toString are
// GetHashCode/Equals/ToString; IllegalArgumentException is ArgumentException; the code in the UNKNOWN phrase uses
// the invariant culture, as Java's int-to-String conversion is locale-independent.
public class SpdyStreamStatus : IComparable<SpdyStreamStatus>
{

    /**
     * 1 Protocol Error
     */
    public static readonly SpdyStreamStatus PROTOCOL_ERROR =
        new SpdyStreamStatus(1, "PROTOCOL_ERROR");

    /**
     * 2 Invalid Stream
     */
    public static readonly SpdyStreamStatus INVALID_STREAM =
        new SpdyStreamStatus(2, "INVALID_STREAM");

    /**
     * 3 Refused Stream
     */
    public static readonly SpdyStreamStatus REFUSED_STREAM =
        new SpdyStreamStatus(3, "REFUSED_STREAM");

    /**
     * 4 Unsupported Version
     */
    public static readonly SpdyStreamStatus UNSUPPORTED_VERSION =
        new SpdyStreamStatus(4, "UNSUPPORTED_VERSION");

    /**
     * 5 Cancel
     */
    public static readonly SpdyStreamStatus CANCEL =
        new SpdyStreamStatus(5, "CANCEL");

    /**
     * 6 Internal Error
     */
    public static readonly SpdyStreamStatus INTERNAL_ERROR =
        new SpdyStreamStatus(6, "INTERNAL_ERROR");

    /**
     * 7 Flow Control Error
     */
    public static readonly SpdyStreamStatus FLOW_CONTROL_ERROR =
        new SpdyStreamStatus(7, "FLOW_CONTROL_ERROR");

    /**
     * 8 Stream In Use
     */
    public static readonly SpdyStreamStatus STREAM_IN_USE =
        new SpdyStreamStatus(8, "STREAM_IN_USE");

    /**
     * 9 Stream Already Closed
     */
    public static readonly SpdyStreamStatus STREAM_ALREADY_CLOSED =
        new SpdyStreamStatus(9, "STREAM_ALREADY_CLOSED");

    /**
     * 10 Invalid Credentials
     */
    public static readonly SpdyStreamStatus INVALID_CREDENTIALS =
        new SpdyStreamStatus(10, "INVALID_CREDENTIALS");

    /**
     * 11 Frame Too Large
     */
    public static readonly SpdyStreamStatus FRAME_TOO_LARGE =
        new SpdyStreamStatus(11, "FRAME_TOO_LARGE");

    /**
     * Returns the {@link SpdyStreamStatus} represented by the specified code.
     * If the specified code is a defined SPDY status code, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static SpdyStreamStatus ValueOf(int code)
    {
        if (code == 0)
        {
            throw new ArgumentException(
                    "0 is not a valid status code for a RST_STREAM");
        }

        switch (code)
        {
        case 1:
            return PROTOCOL_ERROR;
        case 2:
            return INVALID_STREAM;
        case 3:
            return REFUSED_STREAM;
        case 4:
            return UNSUPPORTED_VERSION;
        case 5:
            return CANCEL;
        case 6:
            return INTERNAL_ERROR;
        case 7:
            return FLOW_CONTROL_ERROR;
        case 8:
            return STREAM_IN_USE;
        case 9:
            return STREAM_ALREADY_CLOSED;
        case 10:
            return INVALID_CREDENTIALS;
        case 11:
            return FRAME_TOO_LARGE;
        }

        return new SpdyStreamStatus(code, "UNKNOWN (" + code.ToString(CultureInfo.InvariantCulture) + ')');
    }

    private readonly int code;

    private readonly string statusPhrase;

    /**
     * Creates a new instance with the specified {@code code} and its
     * {@code statusPhrase}.
     */
    public SpdyStreamStatus(int code, string statusPhrase)
    {
        if (code == 0)
        {
            throw new ArgumentException(
                    "0 is not a valid status code for a RST_STREAM");
        }

        this.statusPhrase = ObjectUtil.CheckNotNull(statusPhrase, "statusPhrase");
        this.code = code;
    }

    /**
     * Returns the code of this status.
     */
    public int Code()
    {
        return code;
    }

    /**
     * Returns the status phrase of this status.
     */
    public string StatusPhrase()
    {
        return statusPhrase;
    }

    public override int GetHashCode()
    {
        return Code();
    }

    public override bool Equals(object o)
    {
        if (!(o is SpdyStreamStatus))
        {
            return false;
        }

        return Code() == ((SpdyStreamStatus) o).Code();
    }

    public override string ToString()
    {
        return StatusPhrase();
    }

    public int CompareTo(SpdyStreamStatus o)
    {
        return Code() - o.Code();
    }
}
