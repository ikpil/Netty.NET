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
using System.Globalization;
using System.Numerics;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Factorial;

/**
 * Handler for a server-side channel.  This handler maintains stateful
 * information which is specific to a certain channel using member variables.
 * Therefore, an instance of this handler can cover only one channel.  You have
 * to create a new handler instance whenever you create a new channel and insert
 * this handler  to avoid a race condition.
 */
// CLR: java.math.BigInteger is System.Numerics.BigInteger and new BigInteger("1") is BigInteger.One; multiply is the *
// operator. System.err.printf("%,d ...%n") formats with the grouping separator of the default locale, which is the "N0"
// format of the current culture followed by a line terminator (Console.Error.WriteLine). printStackTrace() writes the
// exception to Console.Error and ctx.close() is CloseAsync(), whose Task is discarded as Java discards the future.
public class FactorialServerHandler : SimpleChannelInboundHandler<BigInteger>
{
    private BigInteger lastMultiplier = BigInteger.One;
    private BigInteger factorial = BigInteger.One;

    protected override void ChannelRead0(IChannelHandlerContext ctx, BigInteger msg)
    {
        // Calculate the cumulative factorial and send it to the client.
        lastMultiplier = msg;
        factorial = factorial * msg;
        _ = ctx.WriteAndFlushAsync(factorial);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Console.Error.WriteLine(string.Format(CultureInfo.CurrentCulture, "Factorial of {0:N0} is: {1:N0}", lastMultiplier, factorial));
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
