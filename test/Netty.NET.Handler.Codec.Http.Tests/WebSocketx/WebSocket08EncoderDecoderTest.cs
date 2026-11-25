/*
 * Copyright 2019 The Netty Project
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
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

/**
 * Tests the WebSocket08FrameEncoder and Decoder implementation.<br>
 * Checks whether the combination of encoding and decoding yields the original data.<br>
 * Thereby also the masking behavior is checked.
 */
// CLR: the Buffer port has no array() (docs/buffer-porting.md, Array extraction), so initTestData() writes
// binTestData.array()[i] = j as SetByte(i, j), which writes the same octet at the same index without moving the writer
// index; the byte counter j wraps after 255 as the signed Java byte wraps after 127, so the bit patterns are the same.
// readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>() of the Transport EmbeddedChannel,
// assertInstanceOf is Assert.IsAssignableFrom, substring(0, n) is Substring(0, n) and getMessage() is Message. The
// arguments of assertEquals(txt.text(), testStr) and assertEquals(readable, testDataLength) are swapped into xUnit's
// (expected, actual) order.
public class WebSocket08EncoderDecoderTest
{

    private ByteBuf binTestData;
    private string strTestData;

    private const int MAX_TESTDATA_LENGTH = 100 * 1024;

    private void InitTestData()
    {
        binTestData = Unpooled.Buffer(MAX_TESTDATA_LENGTH);
        byte j = 0;
        for (int i = 0; i < MAX_TESTDATA_LENGTH; i++)
        {
            binTestData.SetByte(i, j);
            j++;
        }

        StringBuilder s = new StringBuilder();
        char c = 'A';
        for (int i = 0; i < MAX_TESTDATA_LENGTH; i++)
        {
            s.Append(c);
            c++;
            if (c == 'Z')
            {
                c = 'A';
            }
        }
        strTestData = s.ToString();
    }

    [Fact]
    public void TestWebSocketProtocolViolation()
    {
        // Given
        InitTestData();

        int maxPayloadLength = 255;
        string errorMessage = "Max frame length of " + maxPayloadLength + " has been exceeded.";
        WebSocketCloseStatus expectedStatus = WebSocketCloseStatus.MESSAGE_TOO_BIG;

        // With auto-close
        WebSocketDecoderConfig config = WebSocketDecoderConfig.NewBuilder()
            .MaxFramePayloadLength(maxPayloadLength)
            .CloseOnProtocolViolation(true)
            .Build();
        EmbeddedChannel inChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(config));
        EmbeddedChannel outChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(true));

        ExecuteProtocolViolationTest(outChannel, inChannel, maxPayloadLength + 1, expectedStatus, errorMessage);

        CloseWebSocketFrame response = inChannel.ReadOutbound<CloseWebSocketFrame>();
        Assert.NotNull(response);
        Assert.Equal(expectedStatus.Code(), response.StatusCode());
        Assert.Equal(errorMessage, response.ReasonText());
        response.Release();

        Assert.False(inChannel.Finish());
        Assert.False(outChannel.Finish());

        // Without auto-close
        config = WebSocketDecoderConfig.NewBuilder()
            .MaxFramePayloadLength(maxPayloadLength)
            .CloseOnProtocolViolation(false)
            .Build();
        inChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(config));
        outChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(true));

        ExecuteProtocolViolationTest(outChannel, inChannel, maxPayloadLength + 1, expectedStatus, errorMessage);

        response = inChannel.ReadOutbound<CloseWebSocketFrame>();
        Assert.Null(response);

        Assert.False(inChannel.Finish());
        Assert.False(outChannel.Finish());

        // Release test data
        binTestData.Release();
    }

    private void ExecuteProtocolViolationTest(EmbeddedChannel outChannel, EmbeddedChannel inChannel,
            int testDataLength, WebSocketCloseStatus expectedStatus, string errorMessage)
    {
        CorruptedWebSocketFrameException corrupted = null;

        try
        {
            TestBinaryWithLen(outChannel, inChannel, testDataLength);
        }
        catch (CorruptedWebSocketFrameException e)
        {
            corrupted = e;
        }

        BinaryWebSocketFrame exceedingFrame = inChannel.ReadInbound<BinaryWebSocketFrame>();
        Assert.Null(exceedingFrame);

        Assert.NotNull(corrupted);
        Assert.Equal(expectedStatus, corrupted.CloseStatus());
        Assert.Equal(errorMessage, corrupted.Message);
    }

    [Fact]
    public void TestWebSocketEncodingAndDecoding()
    {
        InitTestData();

        // Test without masking
        EmbeddedChannel outChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(false));
        EmbeddedChannel inChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(false, false, 1024 * 1024, false));
        ExecuteTests(outChannel, inChannel);

        // Test with activated masking
        outChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(true));
        inChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(true, false, 1024 * 1024, false));
        ExecuteTests(outChannel, inChannel);

        // Test with activated masking and an unmasked expecting but forgiving decoder
        outChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(true));
        inChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(false, false, 1024 * 1024, true));
        ExecuteTests(outChannel, inChannel);

        // Release test data
        binTestData.Release();
    }

    private void ExecuteTests(EmbeddedChannel outChannel, EmbeddedChannel inChannel)
    {
        // Test at the boundaries of each message type, because this shifts the position of the mask field
        // Test min. 4 lengths to check for problems related to an uneven frame length
        ExecuteTests(outChannel, inChannel, 0);
        ExecuteTests(outChannel, inChannel, 1);
        ExecuteTests(outChannel, inChannel, 2);
        ExecuteTests(outChannel, inChannel, 3);
        ExecuteTests(outChannel, inChannel, 4);
        ExecuteTests(outChannel, inChannel, 5);

        ExecuteTests(outChannel, inChannel, 125);
        ExecuteTests(outChannel, inChannel, 126);
        ExecuteTests(outChannel, inChannel, 127);
        ExecuteTests(outChannel, inChannel, 128);
        ExecuteTests(outChannel, inChannel, 129);

        ExecuteTests(outChannel, inChannel, 65535);
        ExecuteTests(outChannel, inChannel, 65536);
        ExecuteTests(outChannel, inChannel, 65537);
        ExecuteTests(outChannel, inChannel, 65538);
        ExecuteTests(outChannel, inChannel, 65539);
    }

    private void ExecuteTests(EmbeddedChannel outChannel, EmbeddedChannel inChannel, int testDataLength)
    {
        TestTextWithLen(outChannel, inChannel, testDataLength);
        TestBinaryWithLen(outChannel, inChannel, testDataLength);
    }

    private void TestTextWithLen(EmbeddedChannel outChannel, EmbeddedChannel inChannel, int testDataLength)
    {
        string testStr = strTestData.Substring(0, testDataLength);
        outChannel.WriteOutbound(new TextWebSocketFrame(testStr));

        Transfer(outChannel, inChannel);

        object decoded = inChannel.ReadInbound<object>();
        Assert.NotNull(decoded);
        Assert.IsAssignableFrom<TextWebSocketFrame>(decoded);
        TextWebSocketFrame txt = (TextWebSocketFrame) decoded;
        Assert.Equal(testStr, txt.Text());
        txt.Release();
    }

    private void TestBinaryWithLen(EmbeddedChannel outChannel, EmbeddedChannel inChannel, int testDataLength)
    {
        binTestData.Retain(); // need to retain for sending and still keeping it
        binTestData.SetIndex(0, testDataLength); // Send only len bytes
        outChannel.WriteOutbound(new BinaryWebSocketFrame(binTestData));

        Transfer(outChannel, inChannel);

        object decoded = inChannel.ReadInbound<object>();
        Assert.NotNull(decoded);
        Assert.IsAssignableFrom<BinaryWebSocketFrame>(decoded);
        BinaryWebSocketFrame binFrame = (BinaryWebSocketFrame) decoded;
        int readable = binFrame.Content.ReadableBytes;
        Assert.Equal(testDataLength, readable);
        for (int i = 0; i < testDataLength; i++)
        {
            Assert.Equal(binTestData.GetByte(i), binFrame.Content.GetByte(i));
        }
        binFrame.Release();
    }

    private void Transfer(EmbeddedChannel outChannel, EmbeddedChannel inChannel)
    {
        // Transfer encoded data into decoder
        // Loop because there might be multiple frames (gathering write)
        for (;;)
        {
            ByteBuf encoded = outChannel.ReadOutbound<ByteBuf>();
            if (encoded == null)
            {
                return;
            }
            inChannel.WriteInbound(encoded);
        }
    }
}
