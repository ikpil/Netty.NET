/*
 * Copyright 2020 The Netty Project
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
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Dns.Udp;

// CLR: new InetSocketAddress(DNS_SERVER_HOST, DNS_SERVER_PORT) with an IP literal is an IPEndPoint of the parsed
// address. main blocks on bind(0).sync(), writeAndFlush(query).sync() and close().sync(); the async Main awaits those
// Tasks (docs/transport-porting.md: sync() is awaiting the Task), and bind(0).sync().channel() is the result of
// BindAsync. closeFuture().await(10, TimeUnit.SECONDS) is Task.WhenAny over the CloseFuture and a ten-second
// Task.Delay, which reports whether the channel closed in time without throwing. group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. NioDatagramChannel.class is
// Channel<NioDatagramChannel>(); the anonymous ChannelInitializer is an ActionChannelInitializer and the anonymous
// SimpleChannelInboundHandler the nested ResponseHandler class. The Java interfaces DnsQuery, DnsQuestion, DnsRecord,
// DnsRawRecord and DatagramChannel are IDnsQuery, IDnsQuestion, IDnsRecord, IDnsRawRecord and IDatagramChannel, and
// recordAt infers its type argument in Java (RecordAt<IDnsQuestion> here). System.out.printf with %s%n is
// Console.WriteLine with a composite format.
public sealed class DnsClient
{

    private const string QUERY_DOMAIN = "www.example.com";
    private const int DNS_SERVER_PORT = 53;
    private const string DNS_SERVER_HOST = "8.8.8.8";

    private DnsClient() { }

    private static void HandleQueryResp(DatagramDnsResponse msg)
    {
        if (msg.Count(DnsSection.QUESTION) > 0)
        {
            IDnsQuestion question = msg.RecordAt<IDnsQuestion>(DnsSection.QUESTION, 0);
            Console.WriteLine("name: {0}", question.Name());
        }
        for (int i = 0, count = msg.Count(DnsSection.ANSWER); i < count; i++)
        {
            IDnsRecord record = msg.RecordAt<IDnsRecord>(DnsSection.ANSWER, i);
            if (record.Type() == DnsRecordType.A)
            {
                //just print the IP after query
                IDnsRawRecord raw = (IDnsRawRecord) record;
                Console.WriteLine(NetUtil.BytesToIpAddress(ByteBufUtil.GetBytes(raw.Content)));
            }
        }
    }

    public static async Task Main(string[] args)
    {
        IPEndPoint addr = new IPEndPoint(IPAddress.Parse(DNS_SERVER_HOST), DNS_SERVER_PORT);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioDatagramChannel>()
             .Handler(new ActionChannelInitializer<IDatagramChannel>(ch =>
             {
                 IChannelPipeline p = ch.Pipeline;
                 p.AddLast(new DatagramDnsQueryEncoder())
                 .AddLast(new DatagramDnsResponseDecoder())
                 .AddLast(new ResponseHandler());
             }));
            IChannel ch = await b.BindAsync(0);
            IDnsQuery query = new DatagramDnsQuery(null, addr, 1).SetRecord(
                    DnsSection.QUESTION,
                    new DefaultDnsQuestion(QUERY_DOMAIN, DnsRecordType.A));
            await ch.WriteAndFlushAsync(query);
            bool succ = await Task.WhenAny(ch.CloseFuture, Task.Delay(TimeSpan.FromSeconds(10))) == ch.CloseFuture;
            if (!succ)
            {
                Console.Error.WriteLine("dns query timeout!");
                await ch.CloseAsync();
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed class ResponseHandler : SimpleChannelInboundHandler<DatagramDnsResponse>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramDnsResponse msg)
        {
            try
            {
                HandleQueryResp(msg);
            }
            finally
            {
                _ = ctx.CloseAsync();
            }
        }
    }
}
