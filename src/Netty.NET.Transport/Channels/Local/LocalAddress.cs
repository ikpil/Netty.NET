/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Local;

/**
 * An endpoint in the local transport.  Each endpoint is identified by a unique
 * case-insensitive string.
 */
public sealed class LocalAddress : EndPoint, IComparable<LocalAddress>
{
    public static readonly LocalAddress ANY = new LocalAddress("ANY");

    private readonly string id;
    private readonly string strVal;

    /**
     * Creates a new ephemeral port based on the ID of the specified channel.
     * Note that we prepend an upper-case character so that it never conflicts with
     * the addresses created by a user, which are always lower-cased on construction time.
     */
    internal LocalAddress(IChannel channel)
    {
        StringBuilder buf = new StringBuilder(16);
        buf.Append("local:E");
        buf.Append(((channel.GetHashCode() & 0xFFFFFFFFL) | 0x100000000L).ToString("x", CultureInfo.InvariantCulture));
        buf[7] = ':';
        id = buf.ToString(6, buf.Length - 6);
        strVal = buf.ToString();
    }

    /**
     * Creates a new instance with the specified ID.
     */
    // CLR: toLowerCase() uses the invariant culture (culture-independent text, docs/common-clr-design.md).
    public LocalAddress(string id)
    {
        this.id = ObjectUtil.CheckNonEmptyAfterTrim(id, "id").ToLowerInvariant();
        strVal = "local:" + this.id;
    }

    /**
     * Creates a new instance with a random ID based on the given class.
     */
    public LocalAddress(Type cls) : this(ObjectUtil.CheckNotNull(cls, "cls").Name + '/' + Guid.NewGuid())
    {
    }

    /**
     * Returns the ID of this address.
     */
    public string Id => id;

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(id);
    }

    public override bool Equals(object o)
    {
        if (!(o is LocalAddress))
        {
            return false;
        }

        return id.Equals(((LocalAddress)o).id, StringComparison.Ordinal);
    }

    public int CompareTo(LocalAddress o)
    {
        // CLR: String.compareTo is ordinal; a null argument throws as the Java dereference does.
        return string.CompareOrdinal(id, ObjectUtil.CheckNotNull(o, "o").id);
    }

    public override string ToString()
    {
        return strVal;
    }
}
