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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;

namespace Netty.NET.Handler.Codec.Protobuf.Tests;

// CLR: readOutbound() is ReadOutbound<T>(); the final int locals are const int and the final byte[] buf is a plain
// local. The Java byte literal -2 is unchecked((byte) -2), the same eight bits 0xFE: "Constant expressions are
// evaluated by default in a checked context"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked), and the
// unchecked conversion keeps the low bits
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions);
// (byte) (0x8A & 0xFF) and (byte) (0x80 & 0xFF) are the same bit patterns in both languages.
public class ProtobufVarint32LengthFieldPrependerTest
{

    private EmbeddedChannel ch;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor (https://xunit.net/docs/shared-context); it is private because xUnit1013 reports public non-test
    // methods on a test class.
    public ProtobufVarint32LengthFieldPrependerTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        ch = new EmbeddedChannel(new ProtobufVarint32LengthFieldPrepender());
    }

    [Fact]
    public void TestSize1Varint()
    {
        const int size = 1;
        const int num = 10;
        Assert.Equal(size, ProtobufVarint32LengthFieldPrepender.ComputeRawVarint32Size(num));
        byte[] buf = new byte[size + num];
        //0000 1010
        buf[0] = 0x0A;

        for (int i = size; i < num + size; ++i)
        {
            buf[i] = 1;
        }

        Assert.True(ch.WriteOutbound(WrappedBuffer(buf, size, buf.Length - size)));

        ByteBuf expected = WrappedBuffer(buf);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestSize2Varint()
    {
        const int size = 2;
        const int num = 266;
        Assert.Equal(size, ProtobufVarint32LengthFieldPrepender.ComputeRawVarint32Size(num));
        byte[] buf = new byte[size + num];
        /**
         * 8    A    0    2
         * 1000 1010 0000 0010
         * 0000 1010 0000 0010
         * 0000 0010 0000 1010
         *  000 0010  000 1010
         *
         *  0000 0001 0000 1010
         *  0    1    0    A
         * 266
         */

        buf[0] = (byte) (0x8A & 0xFF);
        buf[1] = 0x02;

        for (int i = size; i < num + size; ++i)
        {
            buf[i] = 1;
        }

        Assert.True(ch.WriteOutbound(WrappedBuffer(buf, size, buf.Length - size)));

        ByteBuf expected = WrappedBuffer(buf);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(actual, expected);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestSize3Varint()
    {
        const int size = 3;
        const int num = 0x4000;
        Assert.Equal(size, ProtobufVarint32LengthFieldPrepender.ComputeRawVarint32Size(num));
        byte[] buf = new byte[size + num];
        /**
         * 8    0    8    0    0    1
         * 1000 0000 1000 0000 0000 0001
         * 0000 0000 0000 0000 0000 0001
         * 0000 0001 0000 0000 0000 0000
         *  000 0001  000 0000  000 0000
         *
         *    0 0000 0100 0000 0000 0000
         *    0    0    4    0    0    0
         *
         */

        buf[0] = (byte) (0x80 & 0xFF);
        buf[1] = (byte) (0x80 & 0xFF);
        buf[2] = 0x01;

        for (int i = size; i < num + size; ++i)
        {
            buf[i] = 1;
        }

        Assert.True(ch.WriteOutbound(WrappedBuffer(buf, size, buf.Length - size)));

        ByteBuf expected = WrappedBuffer(buf);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestSize4Varint()
    {
        const int size = 4;
        const int num = 0x200000;
        Assert.Equal(size, ProtobufVarint32LengthFieldPrepender.ComputeRawVarint32Size(num));
        byte[] buf = new byte[size + num];
        /**
         * 8    0    8    0    8    0    0    1
         * 1000 0000 1000 0000 1000 0000 0000 0001
         * 0000 0000 0000 0000 0000 0000 0000 0001
         * 0000 0001 0000 0000 0000 0000 0000 0000
         *  000 0001  000 0000  000 0000  000 0000
         *
         *    0000 0010 0000 0000 0000 0000 0000
         *    0    2    0    0    0    0    0
         *
         */

        buf[0] = (byte) (0x80 & 0xFF);
        buf[1] = (byte) (0x80 & 0xFF);
        buf[2] = (byte) (0x80 & 0xFF);
        buf[3] = 0x01;

        for (int i = size; i < num + size; ++i)
        {
            buf[i] = 1;
        }

        Assert.True(ch.WriteOutbound(WrappedBuffer(buf, size, buf.Length - size)));

        ByteBuf expected = WrappedBuffer(buf);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestTinyEncode()
    {
        byte[] b = { 4, 1, 1, 1, 1 };
        Assert.True(ch.WriteOutbound(WrappedBuffer(b, 1, b.Length - 1)));

        ByteBuf expected = WrappedBuffer(b);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestRegularDecode()
    {
        byte[] b = new byte[2048];

        for (int i = 2; i < 2048; i ++)
        {
            b[i] = 1;
        }

        b[0] = unchecked((byte) -2);
        b[1] = 15;
        Assert.True(ch.WriteOutbound(WrappedBuffer(b, 2, b.Length - 2)));

        ByteBuf expected = WrappedBuffer(b);
        ByteBuf actual = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }
}
