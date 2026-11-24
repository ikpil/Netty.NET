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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the constructor
// (https://xunit.net/docs/shared-context) and is private (xUnit1013 asks public methods of a test class to be tests).
// readOutbound() is ReadOutbound<ByteBuf>() and the Buffer port's addComponent(true, buffer) is AddComponent(buffer,
// true). The short byte[] initializers keep the bit patterns of the Java byte literals: negative values are
// unchecked((byte) ...) and char literals are cast to byte, because only int constants in range convert implicitly
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types); the long
// signed literal of testInputBufferOverseek is an sbyte array that Bytes() reinterprets as System.Byte values, as the
// codec-dns tests do (test/Netty.NET.Handler.Codec.Dns.Tests/DnsResponseTest.cs).
public class SnappyFrameEncoderTest
{
    private EmbeddedChannel channel;

    public SnappyFrameEncoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        channel = new EmbeddedChannel(new SnappyFrameEncoder());
    }

    private static byte[] Bytes(sbyte[] p)
    {
        return Array.ConvertAll(p, b => unchecked((byte) b));
    }

    [Fact]
    public void TestSmallAmountOfDataIsUncompressed()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        channel.WriteOutbound(@in);
        Assert.True(channel.Finish());
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
             0x01, 0x09, 0x00, 0x00, 0x6f, unchecked((byte) -0x68), 0x2e, unchecked((byte) -0x47), (byte) 'n',
             (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });
        ByteBuf actual = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(expected, actual);

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestLargeAmountOfDataIsCompressed()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', (byte) 'n', (byte) 'e', (byte) 't', (byte) 't',
            (byte) 'y',
            (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', (byte) 'n', (byte) 'e', (byte) 't', (byte) 't',
            (byte) 'y'
        });

        channel.WriteOutbound(@in);
        Assert.True(channel.Finish());

        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
             0x00, 0x0E, 0x00, 0x00, 0x3b, 0x36, unchecked((byte) -0x7f), 0x37,
                   0x14, 0x10,
                   (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y',
                   0x3a, 0x05, 0x00
        });
        ByteBuf actual = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(expected, actual);

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestStreamStartIsOnlyWrittenOnce()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        channel.WriteOutbound(@in.Retain());
        @in.ResetReaderIndex(); // rewind the buffer to write the same data
        channel.WriteOutbound(@in);
        Assert.True(channel.Finish());

        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
             0x01, 0x09, 0x00, 0x00, 0x6f, unchecked((byte) -0x68), 0x2e, unchecked((byte) -0x47), (byte) 'n',
             (byte) 'e', (byte) 't', (byte) 't', (byte) 'y',
             0x01, 0x09, 0x00, 0x00, 0x6f, unchecked((byte) -0x68), 0x2e, unchecked((byte) -0x47), (byte) 'n',
             (byte) 'e', (byte) 't', (byte) 't', (byte) 'y',
        });

        CompositeByteBuf actual = Unpooled.CompositeBuffer();
        for (;;)
        {
            ByteBuf m = channel.ReadOutbound<ByteBuf>();
            if (m == null)
            {
                break;
            }
            actual.AddComponent(m, true);
        }
        Assert.Equal(expected, actual);

        expected.Release();
        actual.Release();
    }

    /**
     * This test asserts that if we have a remainder after emitting a copy that
     * is less than 4 bytes (ie. the minimum required for a copy), we should
     * emit a literal rather than trying to see if we can emit another copy.
     */
    [Fact]
    public void TestInputBufferOverseek()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(Bytes(new sbyte[] {
             11,    0, // literal
              0,    0,    0,    0, // 1st copy
             16,   65,   96,  119, -22,   79,  -43,   76,  -75,  -93,
             11,  104,   96,  -99, 126,  -98,   27,  -36,   40,  117,
            -65,   -3,  -57,  -83, -58,    7,  114,  -14,   68, -122,
             124,  88,  118,   54,  45,  -26,  117,   13,  -45,   -9,
             60,  -73,  -53,  -44,  53,   68,  -77,  -71,  109,   43,
             -38,  59,  100,  -12, -87,   44, -106,  123, -107,   38,
             13, -117,  -23,  -49,  29,   21,   26,   66,    1,   -1,
             -1, // literal
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1, // 2nd copy
             66,    0, -104,  -49,  16, -120,   22,    8,  -52,  -54,
           -102,  -52, -119, -124, -92,  -71,  101, -120,  -52,  -48,
             45,  -26,  -24,   26,  41,  -13,   36,   64,  -47,   15,
           -124,   -7,  -16,   91,  96,    0,  -93,  -42,  101,   20,
            -74,   39, -124,   35,  43,  -49,  -21,  -92,  -20,  -41,
             79,   41,  110, -105,  42,  -96,   90,   -9, -100,  -22,
            -62,   91,    2,   35, 113,  117,  -71,   66,    1, // literal
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1,   -1,   -1,  -1,   -1,   -1,   -1,   -1,
             -1,   -1,   -1, // copy
             -1,   1 // remainder
        }));

        channel.WriteOutbound(@in);
        Assert.True(channel.Finish());
        ByteBuf @out = channel.ReadOutbound<ByteBuf>();
        @out.Release();
    }
}
