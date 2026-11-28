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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Json;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests.Json;

public class JsonObjectDecoderTest
{
    [Fact]
    public void TestJsonObjectOverMultipleWrites()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string objectPart1 = "{ \"firstname\": \"John";
        string objectPart2 = "\" ,\n \"surname\" :";
        string objectPart3 = "\"Doe\", age:22   \n}";

        // Test object
        ch.WriteInbound(Unpooled.CopiedBuffer("  \n\n  " + objectPart1, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(objectPart2, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(objectPart3 + "   \n\n  \n", Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(objectPart1 + objectPart2 + objectPart3, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestMultipleJsonObjectsOverMultipleWrites()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string objectPart1 = "{\"name\":\"Jo";
        string objectPart2 = "hn\"}{\"name\":\"John\"}{\"name\":\"Jo";
        string objectPart3 = "hn\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(objectPart1, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(objectPart2, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(objectPart3, Encoding.UTF8));

        for (int i = 0; i < 3; i++)
        {
            ByteBuf res = ch.ReadInbound<ByteBuf>();
            Assert.Equal("{\"name\":\"John\"}", res.ToString(Encoding.UTF8));
            res.Release();
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestJsonArrayOverMultipleWrites()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string arrayPart1 = "[{\"test";
        string arrayPart2 = "case\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"";
        string arrayPart3 = "  }\n\n    , ";
        string arrayPart4 = "{\"testcase\" : \"Streaming string me";
        string arrayPart5 = "ssage\"} ]";

        // Test array
        ch.WriteInbound(Unpooled.CopiedBuffer("   " + arrayPart1, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(arrayPart2, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(arrayPart3, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(arrayPart4, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(arrayPart5 + "      ", Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(arrayPart1 + arrayPart2 + arrayPart3 + arrayPart4 + arrayPart5, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestStreamJsonArrayOverMultipleWrites1()
    {
        string[] array = new string[] {
                "   [{\"test",
                "case\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"",
                "  }\n\n    , ",
                "{\"testcase\" : \"Streaming string me",
                "ssage\"} ]      "
                };
        string[] result = new string[] {
                "{\"testcase\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"  }",
                "{\"testcase\" : \"Streaming string message\"}"
                };
        DoTestStreamJsonArrayOverMultipleWrites(2, array, result);
    }

    [Fact]
    public void TestStreamJsonArrayOverMultipleWrites2()
    {
        string[] array = new string[] {
                "   [{\"test",
                "case\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"",
                "  }\n\n    , {\"test",
                "case\" : \"Streaming string me",
                "ssage\"} ]      "
                };
        string[] result = new string[] {
                "{\"testcase\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"  }",
                "{\"testcase\" : \"Streaming string message\"}"
                };
        DoTestStreamJsonArrayOverMultipleWrites(2, array, result);
    }

    [Fact]
    public void TestStreamJsonArrayOverMultipleWrites3()
    {
        string[] array = new string[] {
                "   [{\"test",
                "case\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"",
                "  }\n\n    , [{\"test",
                "case\" : \"Streaming string me",
                "ssage\"}] ]      "
                };
        string[] result = new string[] {
                "{\"testcase\"  : \"\\\"}]Escaped dou\\\"ble quotes \\\" in JSON str\\\"ing\"  }",
                "[{\"testcase\" : \"Streaming string message\"}]"
                };
        DoTestStreamJsonArrayOverMultipleWrites(2, array, result);
    }

    private static void DoTestStreamJsonArrayOverMultipleWrites(int indexDataAvailable,
            string[] array, string[] result)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder(true));

        bool dataAvailable = false;

        foreach (string part in array)
        {
            dataAvailable = ch.WriteInbound(Unpooled.CopiedBuffer(part, Encoding.UTF8));

            if (indexDataAvailable > 0)
            {
                Assert.False(dataAvailable);
            }
            else
            {
                Assert.True(dataAvailable);
            }

            indexDataAvailable--;
        }

        foreach (string part in result)
        {
            ByteBuf res = ch.ReadInbound<ByteBuf>();
            Assert.Equal(part, res.ToString(Encoding.UTF8));
            res.Release();
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestSingleByteStream()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string json = "{\"foo\" : {\"bar\" : [{},{}]}}";

        foreach (byte c in Encoding.UTF8.GetBytes(json))
        {
            ch.WriteInbound(Unpooled.CopiedBuffer(new byte[] {c}));
        }

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(json, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestBackslashInString1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());
        // {"foo" : "bar\""}
        string json = "{\"foo\" : \"bar\\\"\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(json, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(json, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestBackslashInString2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());
        // {"foo" : "bar\\"}
        string json = "{\"foo\" : \"bar\\\\\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(json, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(json, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestBackslashInString3()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());
        // {"foo" : "bar\\\""}
        string json = "{\"foo\" : \"bar\\\\\\\"\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(json, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(json, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestMultipleJsonObjectsInOneWrite()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string object1 = "{\"key\" : \"value1\"}",
               object2 = "{\"key\" : \"value2\"}",
               object3 = "{\"key\" : \"value3\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(object1 + object2 + object3, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object1, res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object2, res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object3, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestNonJsonContent1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        try
        {
            Assert.Throws<CorruptedFrameException>(
                    () => ch.WriteInbound(Unpooled.CopiedBuffer("  b [1,2,3]", Encoding.UTF8)));
        }
        finally
        {
            Assert.False(ch.Finish());
        }
    }

    [Fact]
    public void TestNonJsonContent2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("  [1,2,3]  ", Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("[1,2,3]", res.ToString(Encoding.UTF8));
        res.Release();

        try
        {
            Assert.Throws<CorruptedFrameException>(
                    () => ch.WriteInbound(Unpooled.CopiedBuffer(" a {\"key\" : 10}", Encoding.UTF8)));
        }
        finally
        {
            Assert.False(ch.Finish());
        }
    }

    [Fact]
    public void TestMaxObjectLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder(6));

        try
        {
            Assert.Throws<TooLongFrameException>(
                    () => ch.WriteInbound(Unpooled.CopiedBuffer("[2,4,5]", Encoding.UTF8)));
        }
        finally
        {
            Assert.False(ch.Finish());
        }
    }

    [Fact]
    public void TestOneJsonObjectPerWrite()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string object1 = "{\"key\" : \"value1\"}",
               object2 = "{\"key\" : \"value2\"}",
               object3 = "{\"key\" : \"value3\"}";

        ch.WriteInbound(Unpooled.CopiedBuffer(object1, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(object2, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(object3, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object1, res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object2, res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(object3, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestSpecialJsonCharsInString()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        string @object = "{ \"key\" : \"[]{}}\\\"}}'}\"}";
        ch.WriteInbound(Unpooled.CopiedBuffer(@object, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(@object, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestStreamArrayElementsSimple()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder(int.MaxValue, true));

        string array = "[  12, \"bla\"  , 13.4   \t  ,{\"key0\" : [1,2], \"key1\" : 12, \"key2\" : {}} , " +
                       "true, false, null, [\"bla\", {}, [1,2,3]] ]";
        string @object = "{\"bla\" : \"blub\"}";
        ch.WriteInbound(Unpooled.CopiedBuffer(array, Encoding.UTF8));
        ch.WriteInbound(Unpooled.CopiedBuffer(@object, Encoding.UTF8));

        ByteBuf res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("12", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("\"bla\"", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("13.4", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("{\"key0\" : [1,2], \"key1\" : 12, \"key2\" : {}}", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("true", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("false", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("null", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("[\"bla\", {}, [1,2,3]]", res.ToString(Encoding.UTF8));
        res.Release();
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal(@object, res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCorruptedFrameException()
    {
        string part1 = "{\"a\":{\"b\":{\"c\":{ \"d\":\"27301\", \"med\":\"d\", \"path\":\"27310\"} }," +
                " \"status\":\"OK\" } }{\"";
        string part2 = "a\":{\"b\":{\"c\":{\"ory\":[{\"competi\":[{\"event\":[{" + "\"externalI\":{\"external\"" +
                ":[{\"id\":\"O\"} ]";

        EmbeddedChannel ch = new EmbeddedChannel(new JsonObjectDecoder());

        ByteBuf res;

        ch.WriteInbound(Unpooled.CopiedBuffer(part1, Encoding.UTF8));
        res = ch.ReadInbound<ByteBuf>();
        Assert.Equal("{\"a\":{\"b\":{\"c\":{ \"d\":\"27301\", \"med\":\"d\", \"path\":\"27310\"} }, " +
                "\"status\":\"OK\" } }", res.ToString(Encoding.UTF8));
        res.Release();

        ch.WriteInbound(Unpooled.CopiedBuffer(part2, Encoding.UTF8));
        res = ch.ReadInbound<ByteBuf>();

        Assert.Null(res);

        ch.WriteInbound(Unpooled.CopiedBuffer("}}]}]}]}}}}", Encoding.UTF8));
        res = ch.ReadInbound<ByteBuf>();

        Assert.Equal("{\"a\":{\"b\":{\"c\":{\"ory\":[{\"competi\":[{\"event\":[{" + "\"externalI\":{" +
                "\"external\":[{\"id\":\"O\"} ]}}]}]}]}}}}", res.ToString(Encoding.UTF8));
        res.Release();

        Assert.False(ch.Finish());
    }
}
