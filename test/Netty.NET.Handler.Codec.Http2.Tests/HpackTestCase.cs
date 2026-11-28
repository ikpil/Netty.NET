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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: gson is test-only and has no BCL counterpart, so the JSON test cases are read with System.Text.Json (owner
// decision 2026-11-25 for codec-http2: "gson (HpackTestCase JSON) -> System.Text.Json"): JsonDocument "Provides a
// mechanism for examining the structural content of a JSON value without automatically instantiating data values"
// (https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocument?view=net-10.0). The GSON constant
// (FieldNamingPolicy.LOWER_CASE_WITH_UNDERSCORES with the HpackHeaderField type adapter) is the Load(...) mapping:
// each field reads the JSON member of its lower_case_with_underscores name (max_header_table_size,
// sensitive_headers, header_blocks, encoded, headers, dynamic_table, table_size), a missing member keeps the field
// initializer, and the type adapter is HeaderFieldDeserializer.Deserialize(JsonElement), whose JsonParseException is
// System.Text.Json.JsonException and whose JsonObject entry set is JsonElement.EnumerateObject() ("Gets an enumerator
// to enumerate the properties in the JSON object",
// https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.enumerateobject?view=net-10.0).
// InputStream is System.IO.Stream, written qualified because Stream binds to the namespace Netty.NET.Handler.Stream
// inside Netty.NET.Handler.*. throw new AssertionError(...) is Xunit.Sdk.XunitException (an assertion failure) and
// throw new Error(...) UnreachableException (docs/codec-http-porting.md, Shared CLR adaptations); the Lists printed in
// the failure messages use the AbstractCollection text "[a, b]" (the private Str). Arrays.equals(byte[], byte[]) is
// SequenceEqual. The String header names and values are StringCharSequence values. The anonymous SensitivityDetector
// is the private nested FixedSensitivityDetector. The package-private class and members are internal.
internal sealed class HpackTestCase
{

    internal int maxHeaderTableSize = -1;
    internal bool sensitiveHeaders;

    internal List<HeaderBlock> headerBlocks;

    private HpackTestCase()
    {
    }

    internal static HpackTestCase Load(System.IO.Stream @is)
    {
        using JsonDocument document = JsonDocument.Parse(@is);
        JsonElement root = document.RootElement;
        HpackTestCase hpackTestCase = new HpackTestCase();

        if (root.TryGetProperty("max_header_table_size", out JsonElement maxHeaderTableSize))
        {
            hpackTestCase.maxHeaderTableSize = maxHeaderTableSize.GetInt32();
        }

        if (root.TryGetProperty("sensitive_headers", out JsonElement sensitiveHeaders))
        {
            hpackTestCase.sensitiveHeaders = sensitiveHeaders.GetBoolean();
        }

        hpackTestCase.headerBlocks = new List<HeaderBlock>();

        foreach (JsonElement block in root.GetProperty("header_blocks").EnumerateArray())
        {
            hpackTestCase.headerBlocks.Add(HeaderBlock.Load(block));
        }

        foreach (HeaderBlock headerBlock in hpackTestCase.headerBlocks)
        {
            headerBlock.encodedBytes = StringUtil.DecodeHexDump(headerBlock.GetEncodedStr());
        }

        return hpackTestCase;
    }

    internal void TestCompress()
    {
        HpackEncoder hpackEncoder = CreateEncoder();

        foreach (HeaderBlock headerBlock in headerBlocks)
        {

            byte[] actual =
                    Encode(hpackEncoder, headerBlock.GetHeaders(), headerBlock.GetMaxHeaderTableSize(),
                            sensitiveHeaders);

            if (!actual.AsSpan().SequenceEqual(headerBlock.encodedBytes))
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED:\n" + headerBlock.GetEncodedStr() +
                                "\nACTUAL:\n" + StringUtil.ToHexString(actual));
            }

            List<HpackHeaderField> actualDynamicTable = new List<HpackHeaderField>();

            for (int index = 0; index < hpackEncoder.Length(); index++)
            {
                actualDynamicTable.Add(hpackEncoder.GetHeaderField(index));
            }

            List<HpackHeaderField> expectedDynamicTable = headerBlock.GetDynamicTable();

            if (!HeadersEqual(expectedDynamicTable, actualDynamicTable))
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED DYNAMIC TABLE:\n" + Str(expectedDynamicTable) +
                                "\nACTUAL DYNAMIC TABLE:\n" + Str(actualDynamicTable));
            }

            if (headerBlock.GetTableSize() != hpackEncoder.Size())
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED TABLE SIZE: " + headerBlock.GetTableSize() +
                                "\n ACTUAL TABLE SIZE : " + hpackEncoder.Size());
            }
        }
    }

    internal void TestDecompress()
    {
        HpackDecoder hpackDecoder = CreateDecoder();

        foreach (HeaderBlock headerBlock in headerBlocks)
        {

            List<HpackHeaderField> actualHeaders = Decode(hpackDecoder, headerBlock.encodedBytes);

            List<HpackHeaderField> expectedHeaders = new List<HpackHeaderField>();

            foreach (HpackHeaderField h in headerBlock.GetHeaders())
            {
                expectedHeaders.Add(new HpackHeaderField(h.name, h.value));
            }

            if (!HeadersEqual(expectedHeaders, actualHeaders))
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED:\n" + Str(expectedHeaders) +
                                "\nACTUAL:\n" + Str(actualHeaders));
            }

            List<HpackHeaderField> actualDynamicTable = new List<HpackHeaderField>();

            for (int index = 0; index < hpackDecoder.Length(); index++)
            {
                actualDynamicTable.Add(hpackDecoder.GetHeaderField(index));
            }

            List<HpackHeaderField> expectedDynamicTable = headerBlock.GetDynamicTable();

            if (!HeadersEqual(expectedDynamicTable, actualDynamicTable))
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED DYNAMIC TABLE:\n" + Str(expectedDynamicTable) +
                                "\nACTUAL DYNAMIC TABLE:\n" + Str(actualDynamicTable));
            }

            if (headerBlock.GetTableSize() != hpackDecoder.Size())
            {
                throw new Xunit.Sdk.XunitException(
                        "\nEXPECTED TABLE SIZE: " + headerBlock.GetTableSize() +
                                "\n ACTUAL TABLE SIZE : " + hpackDecoder.Size());
            }
        }
    }

    private HpackEncoder CreateEncoder()
    {
        int maxHeaderTableSize = this.maxHeaderTableSize;
        if (maxHeaderTableSize == -1)
        {
            maxHeaderTableSize = int.MaxValue;
        }

        try
        {
            return NewTestEncoder(true, MAX_HEADER_LIST_SIZE, maxHeaderTableSize);
        }
        catch (Http2Exception e)
        {
            throw new UnreachableException("invalid initial values!", e);
        }
    }

    private HpackDecoder CreateDecoder()
    {
        int maxHeaderTableSize = this.maxHeaderTableSize;
        if (maxHeaderTableSize == -1)
        {
            maxHeaderTableSize = int.MaxValue;
        }

        return new HpackDecoder(DEFAULT_HEADER_LIST_SIZE, maxHeaderTableSize);
    }

    private static byte[] Encode(HpackEncoder hpackEncoder, List<HpackHeaderField> headers, int maxHeaderTableSize,
                                 bool sensitive)
    {
        IHttp2Headers http2Headers = ToHttp2Headers(headers);
        IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector = new FixedSensitivityDetector(sensitive);
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            if (maxHeaderTableSize != -1)
            {
                hpackEncoder.SetMaxHeaderTableSize(maxHeaderTableSize);
            }

            hpackEncoder.EncodeHeaders(3 /* randomly chosen */, buffer, http2Headers, sensitivityDetector);
            byte[] bytes = new byte[buffer.ReadableBytes];
            buffer.ReadBytes(bytes);
            return bytes;
        }
        finally
        {
            buffer.Release();
        }
    }

    // CLR: the anonymous SensitivityDetector of encode(...), which returns the captured sensitive flag.
    private sealed class FixedSensitivityDetector : IHttp2HeadersEncoder.ISensitivityDetector
    {
        private readonly bool sensitive;

        internal FixedSensitivityDetector(bool sensitive)
        {
            this.sensitive = sensitive;
        }

        public bool IsSensitive(ICharSequence name, ICharSequence value)
        {
            return sensitive;
        }
    }

    private static IHttp2Headers ToHttp2Headers(List<HpackHeaderField> inHeaders)
    {
        IHttp2Headers headers = new DefaultHttp2Headers(false);

        foreach (HpackHeaderField e in inHeaders)
        {
            headers.Add(e.name, e.value);
        }

        return headers;
    }

    private static List<HpackHeaderField> Decode(HpackDecoder hpackDecoder, byte[] expected)
    {
        ByteBuf @in = Unpooled.WrappedBuffer(expected);

        try
        {
            List<HpackHeaderField> headers = new List<HpackHeaderField>();
            TestHeaderListener listener = new TestHeaderListener(headers);
            hpackDecoder.Decode(0, @in, listener, true);
            return headers;
        }
        finally
        {
            @in.Release();
        }
    }

    private static string Concat(List<string> l)
    {
        StringBuilder ret = new StringBuilder();

        foreach (string s in l)
        {
            ret.Append(s);
        }

        return ret.ToString();
    }

    private static bool HeadersEqual(List<HpackHeaderField> expected, List<HpackHeaderField> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (!expected[i].EqualsForTest(actual[i]))
            {
                return false;
            }
        }

        return true;
    }

    // CLR: AbstractCollection.toString() of the lists in the failure messages.
    private static string Str(List<HpackHeaderField> list)
    {
        return "[" + string.Join(", ", list) + "]";
    }

    // CLR: the gson field mapping of the type comment is HeaderBlock.Load(JsonElement); the field that gson only reads
    // (@SuppressWarnings("FieldMayBeFinal")) needs no annotation.
    internal sealed class HeaderBlock
    {
        private int maxHeaderTableSize = -1;
        internal byte[] encodedBytes;
        private List<string> encoded;
        private List<HpackHeaderField> headers;
        private List<HpackHeaderField> dynamicTable;
        private int tableSize;

        internal static HeaderBlock Load(JsonElement json)
        {
            HeaderBlock block = new HeaderBlock();

            if (json.TryGetProperty("max_header_table_size", out JsonElement maxHeaderTableSize))
            {
                block.maxHeaderTableSize = maxHeaderTableSize.GetInt32();
            }

            if (json.TryGetProperty("encoded", out JsonElement encoded))
            {
                block.encoded = new List<string>();

                foreach (JsonElement line in encoded.EnumerateArray())
                {
                    block.encoded.Add(line.GetString());
                }
            }

            block.headers = HeaderFields(json, "headers");
            block.dynamicTable = HeaderFields(json, "dynamic_table");

            if (json.TryGetProperty("table_size", out JsonElement tableSize))
            {
                block.tableSize = tableSize.GetInt32();
            }

            return block;
        }

        private static List<HpackHeaderField> HeaderFields(JsonElement json, string name)
        {
            if (!json.TryGetProperty(name, out JsonElement array))
            {
                return null;
            }

            List<HpackHeaderField> fields = new List<HpackHeaderField>();

            foreach (JsonElement field in array.EnumerateArray())
            {
                fields.Add(HeaderFieldDeserializer.Deserialize(field));
            }

            return fields;
        }

        internal int GetMaxHeaderTableSize()
        {
            return maxHeaderTableSize;
        }

        public string GetEncodedStr()
        {
            return Concat(encoded).Replace(" ", "");
        }

        public List<HpackHeaderField> GetHeaders()
        {
            return headers;
        }

        public List<HpackHeaderField> GetDynamicTable()
        {
            return dynamicTable;
        }

        public int GetTableSize()
        {
            return tableSize;
        }
    }

    // CLR: JsonDeserializer<HpackHeaderField> is the static Deserialize(JsonElement) that HeaderBlock.Load calls for
    // each header object; the unused Type and JsonDeserializationContext parameters of gson have no counterpart.
    internal static class HeaderFieldDeserializer
    {

        public static HpackHeaderField Deserialize(JsonElement json)
        {
            List<JsonProperty> entrySet = new List<JsonProperty>(json.EnumerateObject());
            if (entrySet.Count != 1)
            {
                throw new JsonException("JSON Object has multiple entries: [" + string.Join(", ", entrySet) + "]");
            }

            JsonProperty entry = entrySet[0];
            string name = entry.Name;
            string value = entry.Value.GetString();
            return new HpackHeaderField(new StringCharSequence(name), new StringCharSequence(value));
        }
    }
}
