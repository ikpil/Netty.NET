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
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Netty.NET.Transport.Channels;
// CLR: Guid supplies process-independent 128-bit identity; Java's MAC/PID/time byte layout is not a wire protocol.
/**
 * The default {@link ChannelId} implementation.
 */
[JsonConverter(typeof(Converter))]
public sealed class DefaultChannelId : IChannelId
{
    private readonly Guid value;
    private readonly string longValue;
    private DefaultChannelId(Guid value) { this.value = value; longValue = value.ToString("N"); }
    /**
     * Returns a new {@link DefaultChannelId} instance.
     */
    public static DefaultChannelId NewInstance() => new(Guid.NewGuid());
    public string AsShortText() => longValue[..8];
    public string AsLongText() => longValue;
    public int CompareTo(IChannelId o) => o == null ? 1 : string.CompareOrdinal(AsLongText(), o.AsLongText());
    public override bool Equals(object obj) => obj is DefaultChannelId other && value == other.value;
    public override int GetHashCode() => value.GetHashCode();
    public override string ToString() => AsShortText();

    // CLR: a concrete-type JSON string replaces Java ObjectStream serialization; it contains only the native Guid identity.
    // Interface polymorphism and Java MAC/PID/time bytes are not encoded or inferred.
    public sealed class Converter : JsonConverter<DefaultChannelId>
    {
        public Converter() { }
        public override DefaultChannelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !Guid.TryParseExact(reader.GetString(), "N", out var value))
                throw new JsonException("Expected a 32-character channel Guid.");
            return new DefaultChannelId(value);
        }
        public override void Write(Utf8JsonWriter writer, DefaultChannelId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.AsLongText());
    }
}
