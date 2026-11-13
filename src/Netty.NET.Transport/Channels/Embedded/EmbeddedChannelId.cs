/*
 * Copyright 2014 The Netty Project
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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netty.NET.Transport.Channels.Embedded;

/**
 * A dummy {@link ChannelId} implementation.
 */
[JsonConverter(typeof(Converter))]
internal sealed class EmbeddedChannelId : IChannelId
{
    internal static readonly IChannelId Instance = new EmbeddedChannelId();
    private EmbeddedChannelId() { }
    public string AsShortText() => ToString();
    public string AsLongText() => ToString();
    public int CompareTo(IChannelId o) => o is EmbeddedChannelId ? 0 : string.CompareOrdinal(AsLongText(), o?.AsLongText());
    public override int GetHashCode() => 0;
    public override bool Equals(object obj) => obj is EmbeddedChannelId;
    public override string ToString() => "embedded";

    // CLR: concrete-type JSON round trips preserve the original value equality, hash and comparison across distinct instances.
    public sealed class Converter : JsonConverter<EmbeddedChannelId>
    {
        public Converter() { }
        public override EmbeddedChannelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || reader.GetString() != "embedded")
                throw new JsonException("Expected the embedded channel identity.");
            return new EmbeddedChannelId();
        }
        public override void Write(Utf8JsonWriter writer, EmbeddedChannelId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.AsLongText());
    }
}
