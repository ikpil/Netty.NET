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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Rtsp;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.Rtsp;

/**
 * Test cases for RTSP decoder.
 */
public class RtspDecoderTest
{

    /**
     * There was a problem when an ANNOUNCE request was issued by the server,
     * i.e. entered through the response decoder. First the decoder failed to
     * parse the ANNOUNCE request, then it stopped receiving any more
     * responses. This test verifies that the issue is solved.
     */
    [Fact]
    public void TestReceiveAnnounce()
    {
        byte[] data1 = Encoding.UTF8.GetBytes("ANNOUNCE rtsp://172.20.184.218:554/d3abaaa7-65f2-"
                      + "42b4-8d6b-379f492fcf0f RTSP/1.0\r\n"
                      + "CSeq: 2\r\n"
                      + "Session: 2777476816092819869\r\n"
                      + "x-notice: 5402 \"Session Terminated by Server\" "
                      + "event-date=20150514T075303Z\r\n"
                      + "Range: npt=0\r\n\r\n");

        byte[] data2 = Encoding.UTF8.GetBytes("RTSP/1.0 200 OK\r\n" +
                        "Server: Orbit2x\r\n" +
                        "CSeq: 172\r\n" +
                        "Session: 2547019973447939919\r\n" +
                        "\r\n");

        EmbeddedChannel ch = new EmbeddedChannel(new RtspDecoder(),
                                            new HttpObjectAggregator(1048576));
        ch.WriteInbound(Unpooled.WrappedBuffer(data1),
                        Unpooled.WrappedBuffer(data2));

        IHttpObject res1 = ch.ReadInbound<IHttpObject>();
        Assert.NotNull(res1);
        Assert.IsAssignableFrom<IFullHttpRequest>(res1);
        ((IFullHttpRequest) res1).Release();

        IHttpObject res2 = ch.ReadInbound<IHttpObject>();
        Assert.NotNull(res2);
        Assert.IsAssignableFrom<IFullHttpResponse>(res2);
        ((IFullHttpResponse) res2).Release();
    }

    [Fact]
    public void TestRejectsTrailingNulInMethodName()
    {
        byte[] data = Encoding.Latin1.GetBytes("PLAY\u0000 rtsp://172.20.184.218:554/stream RTSP/1.0\r\n"
            + "CSeq: 1\r\n\r\n");

        EmbeddedChannel ch = new EmbeddedChannel(new RtspDecoder());
        ch.WriteInbound(Unpooled.WrappedBuffer(data));

        IHttpObject res = ch.ReadInbound<IHttpObject>();
        Assert.NotNull(res);
        IHttpRequest req = Assert.IsAssignableFrom<IHttpRequest>(res);
        Assert.False(req.DecoderResult().IsSuccess());

        ch.FinishAndReleaseAll();
    }
}
