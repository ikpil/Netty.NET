/*
 * Copyright 2021 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Dns.Tcp;

// CLR: bootstrap.bind(DNS_SERVER_PORT).channel() takes the channel without waiting for the bind; BindAsync yields the
// channel only as the result of its Task, so the async Main awaits it. Executors.newSingleThreadScheduledExecutor()
// .schedule(Runnable, 1000, TimeUnit.MILLISECONDS) runs the anonymous Runnable once, a second later, on another thread;
// Task.Run over an awaited one-second Task.Delay does that on the thread pool. clientQuery() blocks on connect(...).sync(),
// writeAndFlush(query).sync() and close().sync(); ClientQueryAsync awaits those Tasks (docs/transport-porting.md: sync()
// is awaiting the Task), and closeFuture().await(10, TimeUnit.SECONDS) is Task.WhenAny over the CloseFuture and a
// ten-second Task.Delay. channel.closeFuture().sync() is awaiting the CloseFuture, channel.close() and
// group.shutdownGracefully() discard their Tasks as Java discards the futures, and e.printStackTrace() writes
// Exception.ToString() to Console.Error. The anonymous ChannelInitializers are ActionChannelInitializers, the anonymous
// SimpleChannelInboundHandlers the nested QueryHandler and ResponseHandler classes, and byte[]... is params byte[][].
// The Java interfaces DnsQuery, DnsQuestion, DnsRecord and DnsRawRecord are IDnsQuery, IDnsQuestion, IDnsRecord and
// IDnsRawRecord, and recordAt infers its type argument in Java (RecordAt<IDnsQuestion> here).
public sealed class TcpDnsServer
{
    private const string QUERY_DOMAIN = "www.example.com";
    private const int DNS_SERVER_PORT = 53;
    private const string DNS_SERVER_HOST = "127.0.0.1";
    private static readonly byte[] QUERY_RESULT = new byte[]{(byte) 192, (byte) 168, 1, 1};

    public static async Task Main(string[] args)
    {
        ServerBootstrap bootstrap = new ServerBootstrap().Group(
                new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()))
                .Channel<NioServerSocketChannel>()
                .Handler(new LoggingHandler(LogLevel.INFO))
                .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline.AddLast(new TcpDnsQueryDecoder(), new TcpDnsResponseEncoder(),
                            new QueryHandler());
                }));
        IChannel channel = await bootstrap.BindAsync(DNS_SERVER_PORT);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            try
            {
                await ClientQueryAsync();
                _ = channel.CloseAsync();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }
        });
        await channel.CloseFuture;
    }

    private sealed class QueryHandler : SimpleChannelInboundHandler<IDnsQuery>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx,
                                             IDnsQuery msg)
        {
            IDnsQuestion question = msg.RecordAt<IDnsQuestion>(DnsSection.QUESTION);
            Console.WriteLine("Query domain: " + question);

            //always return 192.168.1.1
            _ = ctx.WriteAndFlushAsync(NewResponse(msg, question, 600, QUERY_RESULT));
        }

        private DefaultDnsResponse NewResponse(IDnsQuery query,
                                               IDnsQuestion question,
                                               long ttl, params byte[][] addresses)
        {
            DefaultDnsResponse response = new DefaultDnsResponse(query.Id());
            response.AddRecord(DnsSection.QUESTION, question);

            foreach (byte[] address in addresses)
            {
                DefaultDnsRawRecord queryAnswer = new DefaultDnsRawRecord(
                        question.Name(),
                        DnsRecordType.A, ttl, Unpooled.WrappedBuffer(address));
                response.AddRecord(DnsSection.ANSWER, queryAnswer);
            }
            return response;
        }
    }

    // copy from TcpDnsClient.java
    private static async Task ClientQueryAsync()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        ch.Pipeline.AddLast(new TcpDnsQueryEncoder())
                                .AddLast(new TcpDnsResponseDecoder())
                                .AddLast(new ResponseHandler());
                    }));

            IChannel ch = await b.ConnectAsync(DNS_SERVER_HOST, DNS_SERVER_PORT);

            int randomID = new Random().Next(60000 - 1000) + 1000;
            IDnsQuery query = new DefaultDnsQuery(randomID, DnsOpCode.QUERY)
                    .SetRecord(DnsSection.QUESTION, new DefaultDnsQuestion(QUERY_DOMAIN, DnsRecordType.A));
            await ch.WriteAndFlushAsync(query);
            bool success = await Task.WhenAny(ch.CloseFuture, Task.Delay(TimeSpan.FromSeconds(10))) == ch.CloseFuture;
            if (!success)
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

    private sealed class ResponseHandler : SimpleChannelInboundHandler<DefaultDnsResponse>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DefaultDnsResponse msg)
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

    private static void HandleQueryResp(DefaultDnsResponse msg)
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
}
