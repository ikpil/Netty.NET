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
using System;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * <p>
 * Launches both Http and Http2 servers using Netty to display a set of images
 * and simulate latency. It is a Netty version of the <a
 * href="https://http2.golang.org/gophertiles?latency=0"> Go lang HTTP2 tiles
 * demo</a>.
 * </p>
 * <p>
 * Please note that if you intent to use the JDK provider for SSL, you MUST use JDK 1.8.
 * Previous JDK versions don't have any cipher suite that is suitable for use with HTTP/2.
 * Alternatively, you can use the OpenSsl provider. Please make sure that you run OpenSsl
 * version 1.0.2 or greater.
 * </p>
 */
// CLR: main(String[]) blocks on http.start().sync(); it is the async Main, which awaits StartAsync() of both servers and
// then the HTTP server's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task). The JDK provider is
// SslStream (OpenSsl is library-not-ported, docs/handler-porting.md). EventLoopGroup is IEventLoopGroup;
// e.printStackTrace() writes the exception to Console.Error and group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class Launcher
{

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        Http2Server http2 = new Http2Server(group);
        HttpServer http = new HttpServer(group);

        try
        {
            await http2.StartAsync();
            Console.Error.WriteLine("Open your web browser and navigate to " + "http://" + Html.IP + ":" + HttpServer.PORT);
            await await http.StartAsync();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
