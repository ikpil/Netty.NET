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

using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Rtsp;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.Rtsp;

/**
 * Test cases for RTSP encoder.
 */
// CLR: CharsetUtil.UTF_8 is Encoding.UTF8; "" + content.length is the invariant text of the length. The HTTP
// message types are the core I-prefixed interfaces, and content() is the Content property.
public class RtspEncoderTest
{

    /**
     * Test of a SETUP request, with no body.
     */
    [Fact]
    public void TestSendSetupRequest()
    {
        string expected = "SETUP rtsp://172.10.20.30:554/d3abaaa7-65f2-42b4-"
                        + "8d6b-379f492fcf0f RTSP/1.0\r\n"
                        + "transport: MP2T/DVBC/UDP;unicast;client=01234567;"
                        + "source=172.10.20.30;"
                        + "destination=1.1.1.1;client_port=6922\r\n"
                        + "cseq: 1\r\n"
                        + "\r\n";

        IHttpRequest request = new DefaultHttpRequest(RtspVersions.RTSP_1_0,
               RtspMethods.SETUP,
               "rtsp://172.10.20.30:554/d3abaaa7-65f2-42b4-8d6b-379f492fcf0f");
        request.Headers().Add(RtspHeaderNames.TRANSPORT,
               "MP2T/DVBC/UDP;unicast;client=01234567;source=172.10.20.30;" +
               "destination=1.1.1.1;client_port=6922");
        request.Headers().Add(RtspHeaderNames.CSEQ, "1");

        EmbeddedChannel ch = new EmbeddedChannel(new RtspEncoder());
        ch.WriteOutbound(request);

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string actual = buf.ToString(Encoding.UTF8);
        buf.Release();
        Assert.Equal(expected, actual);
    }

    /**
     * Test of a GET_PARAMETER request, with body.
     */
    [Fact]
    public void TestSendGetParameterRequest()
    {
        string expected = "GET_PARAMETER rtsp://172.10.20.30:554 RTSP/1.0\r\n"
                        + "session: 2547019973447939919\r\n"
                        + "cseq: 3\r\n"
                        + "content-length: 31\r\n"
                        + "content-type: text/parameters\r\n"
                        + "\r\n"
                        + "stream_state\r\n"
                        + "position\r\n"
                        + "scale\r\n";

        byte[] content = Encoding.UTF8.GetBytes("stream_state\r\n"
                        + "position\r\n"
                        + "scale\r\n");

        IFullHttpRequest request = new DefaultFullHttpRequest(
                RtspVersions.RTSP_1_0,
                RtspMethods.GET_PARAMETER,
                "rtsp://172.10.20.30:554");
        request.Headers().Add(RtspHeaderNames.SESSION, "2547019973447939919");
        request.Headers().Add(RtspHeaderNames.CSEQ, "3");
        request.Headers().Add(RtspHeaderNames.CONTENT_LENGTH,
                "" + content.Length.ToString(CultureInfo.InvariantCulture));
        request.Headers().Add(RtspHeaderNames.CONTENT_TYPE, "text/parameters");
        request.Content.WriteBytes(content);

        EmbeddedChannel ch = new EmbeddedChannel(new RtspEncoder());
        ch.WriteOutbound(request);

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string actual = buf.ToString(Encoding.UTF8);
        buf.Release();
        Assert.Equal(expected, actual);
    }

    /**
     * Test of a 200 OK response, without body.
     */
    [Fact]
    public void TestSend200OkResponseWithoutBody()
    {
        string expected = "RTSP/1.0 200 OK\r\n"
                        + "server: Testserver\r\n"
                        + "cseq: 1\r\n"
                        + "session: 2547019973447939919\r\n"
                        + "\r\n";

        IHttpResponse response = new DefaultHttpResponse(RtspVersions.RTSP_1_0,
                RtspResponseStatuses.OK);
        response.Headers().Add(RtspHeaderNames.SERVER, "Testserver");
        response.Headers().Add(RtspHeaderNames.CSEQ, "1");
        response.Headers().Add(RtspHeaderNames.SESSION, "2547019973447939919");

        EmbeddedChannel ch = new EmbeddedChannel(new RtspEncoder());
        ch.WriteOutbound(response);

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string actual = buf.ToString(Encoding.UTF8);
        buf.Release();
        Assert.Equal(expected, actual);
    }

    /**
     * Test of a 200 OK response, with body.
     */
    [Fact]
    public void TestSend200OkResponseWithBody()
    {
        string expected = "RTSP/1.0 200 OK\r\n"
                        + "server: Testserver\r\n"
                        + "session: 2547019973447939919\r\n"
                        + "content-type: text/parameters\r\n"
                        + "content-length: 50\r\n"
                        + "cseq: 3\r\n"
                        + "\r\n"
                        + "position: 24\r\n"
                        + "stream_state: playing\r\n"
                        + "scale: 1.00\r\n";

        byte[] content = Encoding.UTF8.GetBytes("position: 24\r\n"
                        + "stream_state: playing\r\n"
                        + "scale: 1.00\r\n");

        IFullHttpResponse response =
                new DefaultFullHttpResponse(RtspVersions.RTSP_1_0,
                                            RtspResponseStatuses.OK);
        response.Headers().Add(RtspHeaderNames.SERVER, "Testserver");
        response.Headers().Add(RtspHeaderNames.SESSION, "2547019973447939919");
        response.Headers().Add(RtspHeaderNames.CONTENT_TYPE,
                "text/parameters");
        response.Headers().Add(RtspHeaderNames.CONTENT_LENGTH,
                "" + content.Length.ToString(CultureInfo.InvariantCulture));
        response.Headers().Add(RtspHeaderNames.CSEQ, "3");
        response.Content.WriteBytes(content);

        EmbeddedChannel ch = new EmbeddedChannel(new RtspEncoder());
        ch.WriteOutbound(response);

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string actual = buf.ToString(Encoding.UTF8);
        buf.Release();
        Assert.Equal(expected, actual);
    }
}
