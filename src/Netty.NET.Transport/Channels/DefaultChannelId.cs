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
namespace Netty.NET.Transport.Channels;
// CLR: Guid supplies process-independent 128-bit identity; Java's MAC/PID/time byte layout is not a wire protocol.
/**
 * The default {@link ChannelId} implementation.
 */
public sealed class DefaultChannelId : IChannelId
{
    private readonly Guid _value;
    private readonly string _text;
    private DefaultChannelId(Guid value) { _value = value; _text = value.ToString("N"); }
    /**
     * Returns a new {@link DefaultChannelId} instance.
     */
    public static DefaultChannelId NewInstance() => new(Guid.NewGuid());
    public string AsShortText() => _text[..8];
    public string AsLongText() => _text;
    public int CompareTo(IChannelId other) => other == null ? 1 : string.CompareOrdinal(AsLongText(), other.AsLongText());
    public override bool Equals(object obj) => obj is DefaultChannelId id && _value == id._value;
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => AsShortText();
}
