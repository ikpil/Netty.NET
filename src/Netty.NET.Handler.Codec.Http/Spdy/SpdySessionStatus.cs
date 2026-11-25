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
 * The SPDY session status code and its description.
 */
// CLR: Comparable<SpdySessionStatus> is IComparable<SpdySessionStatus>; hashCode/equals/toString are
// GetHashCode/Equals/ToString; IllegalArgumentException is ArgumentException (docs/codec-base-porting.md); the code in
// the UNKNOWN phrase uses the invariant culture, as Java's int-to-String conversion is locale-independent.
public class SpdySessionStatus : IComparable<SpdySessionStatus>
{

    /**
     * 0 OK
     */
    public static readonly SpdySessionStatus OK =
        new SpdySessionStatus(0, "OK");

    /**
     * 1 Protocol Error
     */
    public static readonly SpdySessionStatus PROTOCOL_ERROR =
        new SpdySessionStatus(1, "PROTOCOL_ERROR");

    /**
     * 2 Internal Error
     */
    public static readonly SpdySessionStatus INTERNAL_ERROR =
        new SpdySessionStatus(2, "INTERNAL_ERROR");

    /**
     * Returns the {@link SpdySessionStatus} represented by the specified code.
     * If the specified code is a defined SPDY status code, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static SpdySessionStatus ValueOf(int code)
    {
        switch (code)
        {
        case 0:
            return OK;
        case 1:
            return PROTOCOL_ERROR;
        case 2:
            return INTERNAL_ERROR;
        }

        return new SpdySessionStatus(code, "UNKNOWN (" + code.ToString(CultureInfo.InvariantCulture) + ')');
    }

    private readonly int code;

    private readonly string statusPhrase;

    /**
     * Creates a new instance with the specified {@code code} and its
     * {@code statusPhrase}.
     */
    public SpdySessionStatus(int code, string statusPhrase)
    {
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
        if (!(o is SpdySessionStatus))
        {
            return false;
        }

        return Code() == ((SpdySessionStatus) o).Code();
    }

    public override string ToString()
    {
        return StatusPhrase();
    }

    public int CompareTo(SpdySessionStatus o)
    {
        return Code() - o.Code();
    }
}
