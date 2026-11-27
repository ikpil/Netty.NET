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
using Netty.NET.Handler.Codec.Socksx;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.SocksProxy;

// CLR: @ChannelHandler.Sharable is the [Sharable] attribute, and channelRead0 is protected as the abstract
// SimpleChannelInboundHandler.ChannelRead0 it overrides (an override cannot change the accessibility). SocksVersion is a
// sealed class with static readonly constants (codec-socks), so the switch over version() matches each constant with a
// case guard. The Java interfaces SocksMessage, Socks4CommandRequest, Socks5InitialRequest, Socks5PasswordAuthRequest,
// Socks5PrivateAuthRequest and Socks5CommandRequest are the I-prefixed interfaces. ctx.write/close return Tasks that
// are discarded as Java discards the futures, and throwable.printStackTrace() writes Exception.ToString() to
// Console.Error.
[Sharable]
public sealed class SocksServerHandler : SimpleChannelInboundHandler<ISocksMessage>
{

    public static readonly SocksServerHandler INSTANCE = new SocksServerHandler();

    private SocksServerHandler()
    {
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, ISocksMessage socksRequest)
    {
        SocksVersion version = socksRequest.Version();
        switch (version)
        {
            case var _ when version == SocksVersion.SOCKS4a:
                ISocks4CommandRequest socksV4CmdRequest = (ISocks4CommandRequest) socksRequest;
                if (socksV4CmdRequest.Type() == Socks4CommandType.CONNECT)
                {
                    ctx.Pipeline.AddLast(new SocksServerConnectHandler());
                    ctx.Pipeline.Remove(this);
                    ctx.FireChannelRead(socksRequest);
                }
                else
                {
                    _ = ctx.CloseAsync();
                }
                break;
            case var _ when version == SocksVersion.SOCKS5:
                if (socksRequest is ISocks5InitialRequest)
                {
                    // auth support example
                    //ctx.pipeline().addFirst(new Socks5PasswordAuthRequestDecoder());
                    //ctx.write(new DefaultSocks5AuthMethodResponse(Socks5AuthMethod.PASSWORD));
                    ctx.Pipeline.AddFirst(new Socks5CommandRequestDecoder());
                    _ = ctx.WriteAsync(new DefaultSocks5InitialResponse(Socks5AuthMethod.NO_AUTH));
                }
                else if (socksRequest is ISocks5PasswordAuthRequest)
                {
                    ctx.Pipeline.AddFirst(new Socks5CommandRequestDecoder());
                    _ = ctx.WriteAsync(new DefaultSocks5PasswordAuthResponse(Socks5PasswordAuthStatus.SUCCESS));
                }
                else if (socksRequest is ISocks5PrivateAuthRequest)
                {
                    ctx.Pipeline.AddFirst(new Socks5CommandRequestDecoder());
                    _ = ctx.WriteAsync(new DefaultSocks5PrivateAuthResponse(Socks5PrivateAuthStatus.SUCCESS));
                }
                else if (socksRequest is ISocks5CommandRequest)
                {
                    ISocks5CommandRequest socks5CmdRequest = (ISocks5CommandRequest) socksRequest;
                    if (socks5CmdRequest.Type() == Socks5CommandType.CONNECT)
                    {
                        ctx.Pipeline.AddLast(new SocksServerConnectHandler());
                        ctx.Pipeline.Remove(this);
                        ctx.FireChannelRead(socksRequest);
                    }
                    else
                    {
                        _ = ctx.CloseAsync();
                    }
                }
                else
                {
                    _ = ctx.CloseAsync();
                }
                break;
            case var _ when version == SocksVersion.UNKNOWN:
                _ = ctx.CloseAsync();
                break;
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception throwable)
    {
        Console.Error.WriteLine(throwable);
        SocksServerUtils.CloseOnFlush(ctx.Channel);
    }
}
