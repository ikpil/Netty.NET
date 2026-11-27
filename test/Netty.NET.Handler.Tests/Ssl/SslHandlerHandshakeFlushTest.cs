/*
 * Copyright 2026 The Netty Project
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

using System.Security.Cryptography;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR supplemental (no Java counterpart): SslHandler flushes the last handshake records it wrapped during an unwrap
// before it fires SslHandshakeCompletionEvent.SUCCESS. Java flushes them at channelReadComplete, after the event. With
// SChannel the last records of a TLS 1.3 server are its NewSessionTicket. A peer that closed as soon as it saw the
// server's handshake event answered the late ticket with an RST, and Windows discards received data on an RST, so the
// server lost the peer's close_notify. SslHandlerTest.testSslCompletionEventsTls13JDK failed intermittently that way
// (docs/handler-porting.md, "Intermittent cases"). The case drives a TLS 1.3 handshake between two EmbeddedChannels and
// checks that, when the server's handshake event fires, the server has already flushed records that the client has not
// read yet.
public class SslHandlerHandshakeFlushTest
{
    [Fact]
    public void ServerFlushesItsLastHandshakeRecordsBeforeTheHandshakeEvent()
    {
        Assert.SkipUnless(SslProvider.IsTlsv13Supported(SslProvider.JDK), "TLSv1.3 is not supported");
        X509Bundle cert = new CertificateBuilder()
                .Subject("CN=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext serverCtx = SslContextBuilder.ForServer((AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificate())
                .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3)
                .Build();
        SslContext clientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3)
                .Build();
        EmbeddedChannel server = new EmbeddedChannel();
        EmbeddedChannel client = new EmbeddedChannel();
        try
        {
            HandshakeEventRecorder recorder = new HandshakeEventRecorder(server);
            server.Pipeline.AddLast(serverCtx.NewHandler(server.Alloc()), recorder);
            client.Pipeline.AddLast(clientCtx.NewHandler(client.Alloc()));

            for (int round = 0; round < 10 && !recorder.Fired; round++)
            {
                Transfer(client, server);
                Transfer(server, client);
            }

            Assert.True(recorder.Fired, "The server did not complete the handshake");
            Assert.True(recorder.FlushedBytesAtEvent > 0,
                    "The server had not flushed its last handshake records when it fired the handshake event");
        }
        finally
        {
            client.FinishAndReleaseAll();
            server.FinishAndReleaseAll();
            ReferenceCountUtil.Release(serverCtx);
            ReferenceCountUtil.Release(clientCtx);
        }
    }

    // Moves the flushed outbound records of one channel to the inbound side of the other.
    private static void Transfer(EmbeddedChannel from, EmbeddedChannel to)
    {
        for (;;)
        {
            ByteBuf record = from.ReadOutbound<ByteBuf>();
            if (record == null)
            {
                return;
            }
            to.WriteInbound(record);
        }
    }

    private sealed class HandshakeEventRecorder : ChannelInboundHandlerAdapter
    {
        private readonly EmbeddedChannel channel;

        internal HandshakeEventRecorder(EmbeddedChannel channel)
        {
            this.channel = channel;
        }

        internal bool Fired { get; private set; }

        internal int FlushedBytesAtEvent { get; private set; }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                Fired = true;
                int bytes = 0;
                foreach (object message in channel.OutboundMessages())
                {
                    if (message is ByteBuf buf)
                    {
                        bytes += buf.ReadableBytes;
                    }
                }
                FlushedBytesAtEvent = bytes;
            }
            ctx.FireUserEventTriggered(evt);
        }
    }
}
