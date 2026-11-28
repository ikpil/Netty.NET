/*
 * Copyright 2020 The Netty Project
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

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: Java ObjectOutputStream/ObjectInputStream serialization has no CLR counterpart; the Transport's channel-ID
// tests use a concrete-type System.Text.Json round trip instead (test/Netty.NET.Transport.Tests/DefaultChannelIdTest.cs
// and EmbeddedChannelIdTest.cs; docs/transport-porting-manifest.json, DefaultChannelId decision 2026-11-16: "The
// concrete ID JSON converters carry the long text; Java binary serialization and arbitrary interface polymorphism are
// not provided"). Http2StreamChannelId carries no JSON converter of its own (its serialVersionUID has no counterpart,
// src/Netty.NET.Handler.Codec.Http2/Http2StreamChannelId.cs), so the test registers the concrete converter
// Http2StreamChannelIdConverter in JsonSerializerOptions.Converters, which takes precedence over a [JsonConverter] on
// the type ("Converter registration precedence",
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to). It writes
// Java's two serialized fields, parentId (through DefaultChannelId's own JSON contract, the long text) and id, and
// reads them into a new instance. The JSON still goes through ByteBufOutputStream/ByteBufInputStream over
// Unpooled.buffer() as in the source; close() is Dispose() (docs/buffer-porting.md, Input/Output stream adapter).
public class Http2StreamChannelIdTest
{

    private static readonly JsonSerializerOptions OPTIONS = NewOptions();

    [Fact]
    public void TestSerialization()
    {
        IChannelId normalInstance = new Http2StreamChannelId(DefaultChannelId.NewInstance(), 0);

        ByteBuf buf = Unpooled.Buffer();
        // CLR: System.IO.Stream is qualified: inside Netty.NET.Handler.* the simple name Stream binds to the
        // Netty.NET.Handler.Stream namespace.
        System.IO.Stream outStream = new ByteBufOutputStream(buf);

        try
        {
            JsonSerializer.Serialize(outStream, (Http2StreamChannelId) normalInstance, OPTIONS);
        }
        finally
        {
            outStream.Dispose();
        }

        System.IO.Stream inStream = new ByteBufInputStream(buf, true);
        IChannelId deserializedInstance;

        try
        {
            deserializedInstance = JsonSerializer.Deserialize<Http2StreamChannelId>(inStream, OPTIONS);
        }
        finally
        {
            inStream.Dispose();
        }

        Assert.Equal(normalInstance, deserializedInstance);
        Assert.Equal(normalInstance.GetHashCode(), deserializedInstance.GetHashCode());
        Assert.Equal(0, normalInstance.CompareTo(deserializedInstance));
        Assert.Equal(normalInstance.AsLongText(), deserializedInstance.AsLongText());
        Assert.Equal(normalInstance.AsShortText(), deserializedInstance.AsShortText());
    }

    // CLR: the JsonSerializerOptions of the ObjectStream replacement (see the class comment).
    private static JsonSerializerOptions NewOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions();
        options.Converters.Add(new Http2StreamChannelIdConverter());
        return options;
    }

    // CLR: the concrete JSON contract of Http2StreamChannelId (see the class comment): {"parentId": <DefaultChannelId
    // long text>, "id": <int>}. The parent's long text is the stream ID's long text without its "/id" suffix
    // (asLongText() is parentId.asLongText() + '/' + id), because the parentId field is private.
    private sealed class Http2StreamChannelIdConverter : JsonConverter<Http2StreamChannelId>
    {
        public override Http2StreamChannelId Read(ref Utf8JsonReader reader, Type typeToConvert,
                                                  JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected an Http2StreamChannelId object.");
            }

            DefaultChannelId parentId = null;
            int? id = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string property = reader.GetString();
                reader.Read();

                if (property == "parentId")
                {
                    parentId = JsonSerializer.Deserialize<DefaultChannelId>(ref reader, options);
                }
                else if (property == "id")
                {
                    id = reader.GetInt32();
                }
                else
                {
                    throw new JsonException("Unexpected property " + property + ".");
                }
            }

            if (parentId == null || id == null)
            {
                throw new JsonException("Expected parentId and id.");
            }

            return new Http2StreamChannelId(parentId, id.Value);
        }

        public override void Write(Utf8JsonWriter writer, Http2StreamChannelId value, JsonSerializerOptions options)
        {
            string longText = value.AsLongText();
            string suffix = "/" + value.GetSequenceId().ToString(CultureInfo.InvariantCulture);
            writer.WriteStartObject();
            writer.WriteString("parentId", longText.Substring(0, longText.Length - suffix.Length));
            writer.WriteNumber("id", value.GetSequenceId());
            writer.WriteEndObject();
        }
    }
}
