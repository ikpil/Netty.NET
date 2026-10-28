/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public sealed class CustomChannelId(int id) : IChannelId
{
    public int CompareTo(IChannelId other) => other is CustomChannelId custom ? id.CompareTo(custom.Value) : string.CompareOrdinal(AsLongText(), other.AsLongText());
    private int Value => id;
    public override int GetHashCode() => id;
    public override bool Equals(object other) => other is CustomChannelId custom && custom.Value == id;
    public override string ToString() => "CustomChannelId " + id;
    public string AsShortText() => ToString();
    public string AsLongText() => ToString();
}
