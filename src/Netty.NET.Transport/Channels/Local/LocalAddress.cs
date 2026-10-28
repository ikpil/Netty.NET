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
using System.Net;

namespace Netty.NET.Transport.Channels.Local;

/**
 * An endpoint in the local transport.  Each endpoint is identified by a unique
 * case-insensitive string.
 */
public sealed class LocalAddress : EndPoint, IComparable<LocalAddress>
{
    public static readonly LocalAddress ANY = new("ANY");
    private readonly string _id;
    /**
     * Creates a new instance with the specified ID.
     */
    /**
     * Creates a new instance with a random ID based on the given class.
     */
    public LocalAddress(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _id = id.Trim().ToLowerInvariant();
        if (_id.Length == 0)
            throw new ArgumentException("Empty local address.", nameof(id));
    }
    public LocalAddress(Type type) : this((type ?? throw new ArgumentNullException(nameof(type))).Name + '/' + Guid.NewGuid()) { }
    /**
     * Creates a new ephemeral port based on the ID of the specified channel.
     * Note that we prepend an upper-case character so that it never conflicts with
     * the addresses created by a user, which are always lower-cased on construction time.
     */
    internal LocalAddress(IChannel channel) { _id = "E:" + channel.Id.AsLongText(); }
    /**
     * Returns the ID of this address.
     */
    public string Id => _id;
    public int CompareTo(LocalAddress other) => other == null ? 1 : string.CompareOrdinal(_id, other._id);
    public override bool Equals(object obj) => obj is LocalAddress address && _id == address._id;
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_id);
    public override string ToString() => "local:" + _id;
}
