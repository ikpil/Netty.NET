/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Encoder for SMTP requests.
 */
// CLR: Class<?> is Type; List<Object> out is IList<object> @out; IllegalStateException is
// InvalidOperationException; the Java byte SP is a byte. List<CharSequence> is IList<ICharSequence>, which is
// always indexable, so the RandomAccess marker test of writeParameters (no CLR counterpart) keeps only its indexed
// branch; the iterator branch for sequential lists has no input. ByteBufUtil.writeAscii(ByteBuf, CharSequence) has
// the Buffer port overloads (ReadOnlySpan<char>) and (AsciiString), so a parameter is written through its
// ToString() text, which gives the same ASCII bytes.
[UnstableApi]
public sealed class SmtpRequestEncoder : MessageToMessageEncoder<object>
{
    private const int CRLF_SHORT = ('\r' << 8) | '\n';
    private const byte SP = (byte) ' ';
    private static readonly ByteBuf DOT_CRLF_BUFFER = LeakPresenceDetector.StaticInitializer(() =>
            Unpooled.UnreleasableBuffer(Unpooled.DirectBuffer(3)
                    .WriteByte('.').WriteByte('\r').WriteByte('\n')).AsReadOnly());

    private bool contentExpected;

    public SmtpRequestEncoder() : base(typeof(object))
    {
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        return msg is ISmtpRequest || msg is ISmtpContent;
    }

    protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
    {
        if (msg is ISmtpRequest)
        {
            ISmtpRequest req = (ISmtpRequest) msg;

            if (contentExpected)
            {
                if (req.Command().Equals(SmtpCommand.RSET))
                {
                    contentExpected = false;
                }
                else
                {
                    throw new InvalidOperationException("SmtpContent expected");
                }
            }

            bool release = true;
            ByteBuf buffer = ctx.Alloc().Buffer();

            try
            {
                req.Command().Encode(buffer);
                bool notEmpty = req.Command() != SmtpCommand.EMPTY;
                WriteParameters(req.Parameters(), buffer, notEmpty);
                ByteBufUtil.WriteShortBE(buffer, CRLF_SHORT);
                @out.Add(buffer);
                release = false;

                if (req.Command().IsContentExpected())
                {
                    contentExpected = true;
                }
            }
            finally
            {
                if (release)
                {
                    buffer.Release();
                }
            }
        }

        if (msg is ISmtpContent)
        {
            if (!contentExpected)
            {
                throw new InvalidOperationException("No SmtpContent expected");
            }

            ByteBuf content = ((ISmtpContent) msg).Content;
            @out.Add(content.Retain());

            if (msg is ILastSmtpContent)
            {
                @out.Add(DOT_CRLF_BUFFER.RetainedDuplicate());
                contentExpected = false;
            }
        }
    }

    private static void WriteParameters(IList<ICharSequence> parameters, ByteBuf @out, bool commandNotEmpty)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        if (commandNotEmpty)
        {
            @out.WriteByte(SP);
        }

        int sizeMinusOne = parameters.Count - 1;

        for (int i = 0; i < sizeMinusOne; i++)
        {
            ByteBufUtil.WriteAscii(@out, parameters[i].ToString());
            @out.WriteByte(SP);
        }

        ByteBufUtil.WriteAscii(@out, parameters[sizeMinusOne].ToString());
    }
}
