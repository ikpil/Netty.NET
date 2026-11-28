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
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.SecureChat;

/**
 * Simple SSL chat server modified from {@link TelnetServer}.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. main(String[]) blocks on bind(PORT).sync() and
// closeFuture().sync(); it is the async Main, which awaits the Task of BindAsync and the channel's CloseFuture.
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// NioServerSocketChannel.class is Channel<NioServerSocketChannel>().
public sealed class SecureChatServer
{

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8992"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        X509Bundle ssc = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext sslCtx = SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
            .Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new SecureChatServerInitializer(sslCtx));

            await (await b.BindAsync(PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
